namespace MPSellerTools.TenantHost.Marketplace;

/// <summary>
/// Host-wide switches for the multichannel catalog ("Marketplace" section).
/// Everything that reaches outside or changes long-standing behaviour is off
/// until someone turns it on.
/// </summary>
public class MarketplaceOptions
{
    public const string SectionName = "Marketplace";

    /// <summary>
    /// Master switch for changing anything on a marketplace. While false every
    /// operation is a dry run, whatever an account's own setting says.
    /// </summary>
    public bool LiveWritesEnabled { get; set; }

    /// <summary>
    /// Whether orders reserve and deduct stock. While false orders leave
    /// stock alone, as they always have, and quantities are not sent to channels.
    /// </summary>
    public bool InventoryAccountingEnabled { get; set; }

    /// <summary>Runs the sync worker inside this host. Turn off to run nothing in the background.</summary>
    public bool WorkerEnabled { get; set; } = true;

    /// <summary>Gives products from before variants existed their default variant at startup.</summary>
    public bool AutoBackfill { get; set; } = true;

    public int WorkerIntervalSeconds { get; set; } = 15;

    /// <summary>How long a worker may hold a job before it is taken to have died.</summary>
    public int LeaseSeconds { get; set; } = 300;

    public int MaxAttempts { get; set; } = 6;

    /// <summary>How long to wait between looks at a submission the channel has accepted but not finished.</summary>
    public int PollSeconds { get; set; } = 60;

    /// <summary>How long an accepted submission may go without a final answer before the job is failed.</summary>
    public int MaxAwaitHours { get; set; } = 24;

    public int OrderImportMinutes { get; set; } = 15;

    /// <summary>Order data older than this is shown as stale, and stock is no longer sent on its strength.</summary>
    public int OrderStaleMinutes { get; set; } = 60;

    public int ReconcileMinutes { get; set; } = 360;

    /// <summary>
    /// How many calls a second are made to any one channel, ahead of the
    /// channel's own throttling. 0 switches the pacing off.
    /// </summary>
    public int RequestsPerSecond { get; set; } = 5;

    /// <summary>How many calls may go out at once after a quiet spell before the pacing takes hold.</summary>
    public int RequestBurst { get; set; } = 10;

    /// <summary>Variants listed under "low stock" on the dashboard when the company has set no alert level of its own.</summary>
    public int DefaultLowStockThreshold { get; set; } = 5;
}
