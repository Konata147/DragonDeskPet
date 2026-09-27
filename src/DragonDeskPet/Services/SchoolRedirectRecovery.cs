namespace DragonDeskPet.Services;

// Never allow or forward an HTTP request. A narrowly scoped server redirect may
// instead trigger one fresh GET to the fixed HTTPS entry, retaining this profile.
public sealed class SchoolRedirectRecovery
{
    private ulong? _trustedNavigation;
    private bool _used;

    public void ResetForManualRetry() { _used = false; _trustedNavigation = null; }

    public bool Observe(ulong id, bool allowed, bool isRedirect, Uri? destination)
    {
        if (allowed) { _trustedNavigation = id; return false; }
        if (_used || _trustedNavigation != id || !isRedirect || destination is null
            || !destination.IsAbsoluteUri || destination.Scheme != Uri.UriSchemeHttp
            || !destination.Host.Equals(new Uri(HnieScheduleAdapter.EntryUrl).Host, StringComparison.OrdinalIgnoreCase)
            || !destination.IsDefaultPort || destination.UserInfo.Length != 0)
            return false;
        _used = true;
        return true;
    }
}
