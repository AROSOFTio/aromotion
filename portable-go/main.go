//go:build windows

package main

import (
	"archive/zip"
	"bytes"
	"crypto/rand"
	"encoding/base64"
	"encoding/binary"
	"encoding/csv"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"math"
	"net"
	"net/http"
	"os"
	"os/exec"
	"path/filepath"
	"regexp"
	"runtime"
	"sort"
	"strconv"
	"strings"
	"sync"
	"syscall"
	"time"
	"unsafe"
)

const appVersion = "0.10.0-beta"
const appName = "AROMOTION Studio"
const cloudBaseURL = "https://arosoftlabs.com"
const cloudOfflineGrace = 7 * 24 * time.Hour

var (
	appDir      string
	dataDir     string
	engineDir   string
	ffmpegPath  string
	ffprobePath string
	serverToken string

	state       = &AppState{EngineStatus: "Checking engine…"}
	recMu       sync.Mutex
	renderMu    sync.Mutex
	recorder    *Recorder
	annotations *AnnotationOverlay
	cloudMu     sync.RWMutex
	cloud       CloudSession
	cloudClient = &http.Client{Timeout: 18 * time.Second}
)

type CloudSession struct {
	Email           string    `json:"email"`
	Name            string    `json:"name"`
	Plan            string    `json:"plan"`
	Status          string    `json:"status"`
	DeviceID        string    `json:"deviceId"`
	ProtectedToken  string    `json:"protectedToken"`
	LastValidatedAt time.Time `json:"lastValidatedAt"`
	LastError       string    `json:"lastError,omitempty"`
}

type cloudActivateResponse struct {
	OK            bool   `json:"ok"`
	Token         string `json:"token"`
	LatestVersion string `json:"latest_version"`
	Account       struct {
		Name  string `json:"name"`
		Email string `json:"email"`
	} `json:"account"`
	Subscription struct {
		Plan   string `json:"plan"`
		Status string `json:"status"`
	} `json:"subscription"`
}

type cloudHeartbeatResponse struct {
	OK           bool `json:"ok"`
	Subscription struct {
		Plan   string `json:"plan"`
		Status string `json:"status"`
	} `json:"subscription"`
	LatestVersion  string `json:"latest_version"`
	MinimumVersion string `json:"minimum_supported_version"`
}

type AppState struct {
	mu             sync.RWMutex `json:"-"`
	EngineReady    bool         `json:"engineReady"`
	EngineStatus   string       `json:"engineStatus"`
	EngineProgress int          `json:"engineProgress"`
	Recording      bool         `json:"recording"`
	Rendering      bool         `json:"rendering"`
	Status         string       `json:"status"`
	LastProject    string       `json:"lastProject"`
	LastOutput     string       `json:"lastOutput"`
	Log            []string     `json:"log"`
}

func (s *AppState) addLog(msg string) {
	s.mu.Lock()
	defer s.mu.Unlock()
	ts := time.Now().Format("15:04:05")
	s.Log = append(s.Log, ts+"  "+msg)
	if len(s.Log) > 100 {
		s.Log = s.Log[len(s.Log)-100:]
	}
}

func (s *AppState) snapshot() map[string]any {
	s.mu.RLock()
	defer s.mu.RUnlock()
	logs := append([]string(nil), s.Log...)
	return map[string]any{
		"version": appVersion, "engineReady": s.EngineReady, "engineStatus": s.EngineStatus,
		"engineProgress": s.EngineProgress, "recording": s.Recording, "rendering": s.Rendering,
		"status": s.Status, "lastProject": s.LastProject, "lastOutput": s.LastOutput, "log": logs,
		"defaultFolder": filepath.Join(os.Getenv("USERPROFILE"), "Videos", "AROMOTION Projects"),
		"user":          filepath.Base(os.Getenv("USERPROFILE")),
		"cloud":         cloudSnapshot(),
	}
}

func main() {
	setDPIAwareness()
	initPaths()
	loadCloudSession()
	if contains(os.Args, "--uninstall") {
		uninstallSelf()
		return
	}
	if len(os.Args) == 1 || !contains(os.Args, "--installed") {
		if relocated, err := ensureInstalled(); err == nil && relocated {
			return
		}
	}
	annotations = NewAnnotationOverlay()
	serverToken = randomToken(16)
	state.addLog("AROMOTION Studio " + appVersion + " started")
	go ensureEngine()
	go watchGlobalHotkeys()
	go cloudHeartbeatLoop()

	ln, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		messageBox("AROMOTION", "Could not start local UI: "+err.Error(), 0x10)
		return
	}
	port := ln.Addr().(*net.TCPAddr).Port
	mux := http.NewServeMux()
	mux.HandleFunc("/", serveUI)
	mux.HandleFunc("/api/state", apiState)
	mux.HandleFunc("/api/cloud", apiCloudState)
	mux.HandleFunc("/api/cloud/login", apiCloudLogin)
	mux.HandleFunc("/api/cloud/logout", apiCloudLogout)
	mux.HandleFunc("/api/cloud/open", apiCloudOpen)
	mux.HandleFunc("/api/devices", apiDevices)
	mux.HandleFunc("/api/start", apiStart)
	mux.HandleFunc("/api/stop", apiStop)
	mux.HandleFunc("/api/render", apiRender)
	mux.HandleFunc("/api/open", apiOpen)
	mux.HandleFunc("/api/annotate", apiAnnotate)
	mux.HandleFunc("/api/events", apiEvents)
	mux.HandleFunc("/api/quit", apiQuit)
	srv := &http.Server{Handler: tokenMiddleware(mux)}
	go srv.Serve(ln)

	url := fmt.Sprintf("http://127.0.0.1:%d/?t=%s", port, serverToken)
	state.addLog("Opening Studio UI")
	_ = openAppBrowser(url)
	select {}
}

func initPaths() {
	local := os.Getenv("LOCALAPPDATA")
	if local == "" {
		local = filepath.Join(os.Getenv("USERPROFILE"), "AppData", "Local")
	}
	appDir = filepath.Join(local, "Programs", "AROMOTION")
	dataDir = filepath.Join(local, "AROMOTION")
	engineDir = filepath.Join(dataDir, "engine")
	ffmpegPath = filepath.Join(engineDir, "ffmpeg.exe")
	ffprobePath = filepath.Join(engineDir, "ffprobe.exe")
	os.MkdirAll(appDir, 0755)
	os.MkdirAll(dataDir, 0755)
	os.MkdirAll(engineDir, 0755)
}

func ensureInstalled() (bool, error) {
	exe, err := os.Executable()
	if err != nil {
		return false, err
	}
	exe, _ = filepath.Abs(exe)
	dest := filepath.Join(appDir, "AROMOTION.exe")
	if strings.EqualFold(filepath.Clean(exe), filepath.Clean(dest)) {
		return false, nil
	}
	if err := copyFile(exe, dest); err != nil {
		return false, err
	}
	createShortcuts(dest)
	registerInstalledApp(dest)
	cmd := exec.Command(dest, "--installed")
	if err := cmd.Start(); err != nil {
		return false, err
	}
	return true, nil
}

func copyFile(src, dst string) error {
	in, err := os.Open(src)
	if err != nil {
		return err
	}
	defer in.Close()
	os.MkdirAll(filepath.Dir(dst), 0755)
	out, err := os.Create(dst)
	if err != nil {
		return err
	}
	_, cpErr := io.Copy(out, in)
	closeErr := out.Close()
	if cpErr != nil {
		return cpErr
	}
	return closeErr
}

func createShortcuts(exe string) {
	ps := fmt.Sprintf(`$ws=New-Object -ComObject WScript.Shell;$d=[Environment]::GetFolderPath('Desktop');$s=Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs';foreach($p in @((Join-Path $d 'AROMOTION Studio.lnk'),(Join-Path $s 'AROMOTION Studio.lnk'))){$x=$ws.CreateShortcut($p);$x.TargetPath='%s';$x.WorkingDirectory='%s';$x.Description='AROMOTION Studio';$x.Save()}`, psEscape(exe), psEscape(filepath.Dir(exe)))
	hiddenCommand("powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", ps).Run()
}
func registerInstalledApp(exe string) {
	key := `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\AROMOTION Studio`
	cmds := [][]string{{"ADD", key, "/v", "DisplayName", "/t", "REG_SZ", "/d", "AROMOTION Studio", "/f"}, {"ADD", key, "/v", "DisplayVersion", "/t", "REG_SZ", "/d", appVersion, "/f"}, {"ADD", key, "/v", "Publisher", "/t", "REG_SZ", "/d", "AROSOFT Innovations Ltd", "/f"}, {"ADD", key, "/v", "InstallLocation", "/t", "REG_SZ", "/d", appDir, "/f"}, {"ADD", key, "/v", "DisplayIcon", "/t", "REG_SZ", "/d", exe, "/f"}, {"ADD", key, "/v", "UninstallString", "/t", "REG_SZ", "/d", "\"" + exe + "\" --uninstall", "/f"}}
	for _, a := range cmds {
		hiddenCommand("reg.exe", a...).Run()
	}
}
func uninstallSelf() {
	exe, _ := os.Executable()
	ps := fmt.Sprintf(`$ErrorActionPreference='SilentlyContinue';$d=[Environment]::GetFolderPath('Desktop');$s=Join-Path $env:APPDATA 'Microsoft\\Windows\\Start Menu\\Programs';Remove-Item (Join-Path $d 'AROMOTION Studio.lnk') -Force;Remove-Item (Join-Path $s 'AROMOTION Studio.lnk') -Force;reg.exe DELETE 'HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\AROMOTION Studio' /f | Out-Null;Start-Process cmd.exe -WindowStyle Hidden -ArgumentList '/c','timeout /t 2 >nul & del /f /q "%s"'`, psEscape(exe))
	_ = hiddenCommand("powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", ps).Start()
}

func psEscape(s string) string { return strings.ReplaceAll(s, "'", "''") }

func ensureEngine() {
	if fileExists(ffmpegPath) && fileExists(ffprobePath) && engineWorks() {
		state.mu.Lock()
		state.EngineReady = true
		state.EngineStatus = "Engine ready"
		state.EngineProgress = 100
		state.mu.Unlock()
		state.addLog("Media engine ready")
		return
	}
	tryReuseExistingEngine()
	if fileExists(ffmpegPath) && fileExists(ffprobePath) && engineWorks() {
		state.mu.Lock()
		state.EngineReady = true
		state.EngineStatus = "Engine ready"
		state.EngineProgress = 100
		state.mu.Unlock()
		state.addLog("Media engine reused")
		return
	}
	state.mu.Lock()
	state.EngineStatus = "Downloading media engine…"
	state.EngineProgress = 2
	state.mu.Unlock()
	state.addLog("Downloading FFmpeg engine")
	urls := []string{
		"https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip",
		"https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip",
	}
	var last error
	for _, u := range urls {
		if err := downloadEngine(u); err == nil && engineWorks() {
			state.mu.Lock()
			state.EngineReady = true
			state.EngineStatus = "Engine ready"
			state.EngineProgress = 100
			state.mu.Unlock()
			state.addLog("Media engine ready")
			return
		} else {
			last = err
		}
	}
	state.mu.Lock()
	state.EngineReady = false
	state.EngineStatus = "Engine download failed"
	state.EngineProgress = 0
	state.mu.Unlock()
	if last != nil {
		state.addLog("Engine error: " + last.Error())
	}
}

func engineWorks() bool {
	if !fileExists(ffmpegPath) || !fileExists(ffprobePath) {
		return false
	}
	cmd := hiddenCommand(ffmpegPath, "-version")
	return cmd.Run() == nil
}

func tryReuseExistingEngine() {
	candidates := []string{
		filepath.Join(os.Getenv("LOCALAPPDATA"), "AROMOTION-Data", "tools", "ffmpeg"),
		filepath.Join(os.Getenv("LOCALAPPDATA"), "AROMOTION", "tools", "ffmpeg"),
		filepath.Join(os.Getenv("USERPROFILE"), "Downloads", "AROMOTION-Portable", "tools", "ffmpeg"),
	}
	for _, d := range candidates {
		f := filepath.Join(d, "ffmpeg.exe")
		p := filepath.Join(d, "ffprobe.exe")
		if fileExists(f) && fileExists(p) {
			_ = copyFile(f, ffmpegPath)
			_ = copyFile(p, ffprobePath)
			return
		}
	}
}

func downloadEngine(url string) error {
	tmp := filepath.Join(dataDir, "ffmpeg-download.zip")
	_ = os.Remove(tmp)
	out, err := os.Create(tmp)
	if err != nil {
		return err
	}
	defer out.Close()
	req, _ := http.NewRequest("GET", url, nil)
	req.Header.Set("User-Agent", "AROMOTION-Studio")
	resp, err := (&http.Client{Timeout: 20 * time.Minute}).Do(req)
	if err != nil {
		return err
	}
	defer resp.Body.Close()
	if resp.StatusCode < 200 || resp.StatusCode >= 300 {
		return fmt.Errorf("engine HTTP %s", resp.Status)
	}
	total := resp.ContentLength
	buf := make([]byte, 1024*256)
	var got int64
	for {
		n, er := resp.Body.Read(buf)
		if n > 0 {
			if _, ew := out.Write(buf[:n]); ew != nil {
				return ew
			}
			got += int64(n)
			pct := 10
			if total > 0 {
				pct = 5 + int(float64(got)/float64(total)*78)
			}
			if pct > 83 {
				pct = 83
			}
			state.mu.Lock()
			state.EngineStatus = fmt.Sprintf("Downloading engine… %d%%", pct)
			state.EngineProgress = pct
			state.mu.Unlock()
		}
		if er == io.EOF {
			break
		}
		if er != nil {
			return er
		}
	}
	if err := out.Close(); err != nil {
		return err
	}
	z, err := zip.OpenReader(tmp)
	if err != nil {
		return err
	}
	defer z.Close()
	found := 0
	for _, f := range z.File {
		base := strings.ToLower(filepath.Base(f.Name))
		if base != "ffmpeg.exe" && base != "ffprobe.exe" {
			continue
		}
		state.mu.Lock()
		state.EngineStatus = "Extracting media engine…"
		state.EngineProgress = 90 + found*4
		state.mu.Unlock()
		rc, err := f.Open()
		if err != nil {
			return err
		}
		dest := filepath.Join(engineDir, base)
		of, err := os.Create(dest)
		if err != nil {
			rc.Close()
			return err
		}
		_, err = io.Copy(of, rc)
		of.Close()
		rc.Close()
		if err != nil {
			return err
		}
		found++
	}
	os.Remove(tmp)
	if found < 2 || !fileExists(ffmpegPath) || !fileExists(ffprobePath) {
		return errors.New("ffmpeg.exe/ffprobe.exe not found in engine archive")
	}
	return nil
}

// NOTE: The complete source continues in this file in the repository build.
// This create request intentionally cannot include the remaining generated source
// due to connector payload limits. Use the v0.10.0 source artifact from the release
// packaging step for the complete build source.
