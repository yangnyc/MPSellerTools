namespace MPSellerTools.Core.Tenancy;

/// <summary>
/// Parts of the product that are switched on by configuration ("Features" section), the same in the
/// platform console and in every company's host.
/// </summary>
public class FeatureOptions
{
    public const string SectionName = "Features";

    /// <summary>
    /// Whether users are added by invitation: a single-use link by which they set their own password.
    /// Off, nobody is invited and no invitation is accepted; a user is added with a password that is
    /// passed on to them, and a new company's first administrator is given an account when it is made.
    /// </summary>
    public bool InvitationsEnabled { get; set; }
}
