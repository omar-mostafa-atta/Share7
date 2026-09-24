namespace Share7.Domain.Constants;

public static class Roles
{
    public const string Student = "Student";
    public const string Teacher = "Teacher";
    public const string Admin = "Admin";
    public const string SuperAdmin = "SuperAdmin";

    /// <summary>
    /// Staff who build the curriculum and author its questions, in the Content Studio.
    /// <para>
    /// <b>It grants nothing on this API.</b> Since cutover the role marks an account as belonging
    /// to the Studio, and is read in order to <i>refuse</i> it: <c>AuthService.LoginAsync</c> turns
    /// it away after the password check, and <c>AuthService.NeverLinkedRoles</c> has never let a
    /// Google or Facebook sign-in attach itself to one. Their work happens on <c>/api/studio</c>,
    /// which reads a Studio token on its own scheme and looks the Studio role up per request.
    /// </para>
    /// <para>
    /// Created by a SuperAdmin in Team &amp; Access, which makes the account and the staff profile
    /// together — not from the Users page, which never made the profile.
    /// </para>
    /// </summary>
    public const string ContentTeam = "ContentTeam";

    public static readonly string[] All = [Student, Teacher, Admin, SuperAdmin, ContentTeam];
}
