namespace NeonVelocity.UI
{
    /// <summary>
    /// Optional contextual Back hook. Screens with unsaved state or an in-place sub-view
    /// (for example an active multiplayer room) implement this instead of pushing a new
    /// history entry. Returning true means the screen fully handled the request.
    /// </summary>
    public interface IScreenBackHandler
    {
        bool HandleBackRequest();
    }
}
