namespace ServiceLib.Events;

public static class AppEvents
{
    public static readonly EventChannel<RxVoid> AddServerViaClipboardRequested = new();
    public static readonly EventChannel<bool> HasUpdateNotified = new();

    public static readonly EventChannel<ServerSpeedItem> DispatcherStatisticsRequested = new();

    public static readonly EventChannel<string> SendSnackMsgRequested = new();
    public static readonly EventChannel<string> SendMsgViewRequested = new();

    public static readonly EventChannel<RxVoid> AppExitRequested = new();
    public static readonly EventChannel<bool> ShutdownRequested = new();

    public static readonly EventChannel<ESysProxyType> SysProxyChangeRequested = new();

    /// <summary>
    /// The active server changed. This is NOT a reload: the core keeps running and
    /// only the row highlight has to follow. SetDefaultServerIndex publishes it so
    /// every path that writes config.IndexId (the grid's own command, a subscription
    /// import, the ConfigHandler fallbacks) moves the highlight. Using the reload
    /// channel here instead would restart the core and cut the traffic.
    /// </summary>
    public static readonly EventChannel<string> ActiveServerChanged = new();
}
