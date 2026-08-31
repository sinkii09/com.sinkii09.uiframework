namespace Sinkii09.UIFramework
{
    // Implemented by the widget a TooltipTrigger sits on (or by any component on the same
    // GameObject). Called at show time, not at bind time, so the payload always reflects current
    // state — a stat that changed since the cell was bound shows its new value.
    //
    // Returning null cancels the show: that is the supported way for a widget to say "nothing to
    // describe right now" without the trigger needing to know why.
    public interface ITooltipSource
    {
        ITooltipPayload GetTooltipPayload();
    }
}
