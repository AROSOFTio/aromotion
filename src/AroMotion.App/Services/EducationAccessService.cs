namespace AroMotion.App.Services;

public sealed class EducationAccessService
{
    private static readonly HashSet<string> ApprovedAcademicDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "my.uopeople.edu",
        "studmc.kiu.ac.ug"
    };

    public EducationAccessResult Verify(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return EducationAccessResult.Invalid("Enter your university-issued email address.");
        }

        email = email.Trim();
        var atIndex = email.LastIndexOf('@');
        if (atIndex <= 0 || atIndex == email.Length - 1)
        {
            return EducationAccessResult.Invalid("Enter a valid email address.");
        }

        var domain = email[(atIndex + 1)..];
        if (ApprovedAcademicDomains.Contains(domain))
        {
            return EducationAccessResult.Approved(
                email,
                "Student Edition — FREE",
                "Verified academic domain. AROMOTION Student Edition is free for this account.");
        }

        return EducationAccessResult.Invalid(
            "This academic domain is not yet on AROMOTION's automatic student list. It can be reviewed and added without exposing individual student addresses in the public source code.");
    }
}

public sealed record EducationAccessResult(
    bool IsApproved,
    string? Email,
    string Plan,
    string Message)
{
    public static EducationAccessResult Approved(string email, string plan, string message) =>
        new(true, email, plan, message);

    public static EducationAccessResult Invalid(string message) =>
        new(false, null, "Not verified", message);
}
