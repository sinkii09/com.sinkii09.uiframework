namespace Sinkii09.UIFramework
{
    // Content handed to a tooltip view. The payload decides which view renders it: a null or empty
    // ViewKey routes to the built-in TooltipView (which understands TooltipContent), any other key
    // routes to whichever TooltipViewBase registered under that key.
    //
    // Payloads come from an ITooltipSource on the widget, not from a central database — a tooltip
    // describes the thing under the pointer, and only that thing knows what it is.
    public interface ITooltipPayload
    {
        string ViewKey { get; }
    }
}
