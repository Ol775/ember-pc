namespace Ember.Core;

/// <summary>Which version of the Terms of Use is current. Raise it when TERMS.md changes in a way people must agree to again.</summary>
public static class Legal
{
    public const int TermsVersion = 1;

    /// <summary>True when the agreement on record (the installer's, or the one given in the app) covers the current terms.</summary>
    public static bool IsAccepted(int acceptedInApp, int acceptedByInstaller) => Math.Max(acceptedInApp, acceptedByInstaller) >= TermsVersion;
}
