# Simphonis.Abp.AuditLogs

This library contains tools whose purpose is to facilitate or automate the cleanup of the ABP audit log.

# Usage

The library contains an ABP module, which can be added to any solution.
By adding it, you may choose to register a worker to run regularly. When this worker runs, it deletes all audit log entries whose age exceeds a configured value, which is one year by default.
It will also make a new service available, `IAuditLogCleaner`, which provides a utility method to manually clean up the audit logs, if the worker option does not work for you.

Note that the library will automatically clean up logs for all tenants if multi-tenancy is enabled.

## Installing the module

Install the library in your project...

```bash
dotnet package add Simphonis.Abp.AuditLogs
```

... and register its module in your project's ABP module

```cs
using Simphonis.Abp.AuditLogs;

[DependsOn(typeof(SimphonisAuditLogsModule))]
public class MyModule : AbpModule
//...
```

## Register the worker
If you just want to regularly clean the logs, the easiest option is to register the `AuditLogsCleanerWorker` class as a background worker:
```cs
public override async Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
{
    await context.AddBackgroundWorkerAsync<AuditLogsCleanerWorker>();
}
```

### Configuration
By default, this worker will run every 10 minutes to clean up the old audit log entries. If you wish to change the interval at which it runs, change
the value in `AuditLogsCleanerWorkerOptions`. For example:
```cs
public override void ConfigureServices(ServiceConfigurationContext context)
{
    Configure<AuditLogsCleanerWorkerOptions>(opts =>
    {
        opts.Period = TimeSpan.FromMinutes(30);
    });
}
```

If for some reason you cannot use this basic worker, then you may use your own tools to call `IAuditLogCleaner` manually.

Additionnaly, the worker uses some configuration values stored in the ABP Settings. They influence the behavior of the scheduled task and have no impact on the direct use of `IAuditLogCleaner`.

| Name | Default | Description |
| --- | --- | --- |
| Simphonis.AuditLogs.AuditLogRetentionPeriod | 365 | Maximum age of an audit log row, in days, after which the worker will try to remove it. |
| Simphonis.AuditLogs.MaxDeleteCount | 10000 | Maximum number of audit log rows that can be removed for each tenant (the host counting as one) in a single worker run. This setting exists to avoid overloading the database if there are a very large number of old rows to delete. Note that this value cannot be set above 1000000. |

These settings are read at the host level, and apply to all tenants.

## Delete batch size
Whether it is called by the worker or directly, `IAuditLogCleaner` deletes the old rows of each tenant oldest first, in batches. Each batch is deleted by its own statement, in its own unit of work, so that the database locks are released between two batches: the application's audit log writes wait at most for one batch, instead of the whole cleanup.

The batch size is set in `AuditLogCleanerOptions` (1000 rows by default, at most 1000000):
```cs
public override void ConfigureServices(ServiceConfigurationContext context)
{
    Configure<AuditLogCleanerOptions>(opts =>
    {
        opts.DeleteBatchSize = 500;
    });
}
```

This was made to limit the chance that large removal of audit log data locks the database and degrades the user experience while it is ongoing.
