# Simphonis.Abp.AuditLogs

This library contains tools whose purpose is to facilitate or automate the cleanup of the ABP audit log.

# Usage

The library contains an ABP module, which can be added to any solution.
By adding it, you may choose to register a worker to run regularly. When this worker runs, it deletes all audit log entries whose age exceeds a configured value, which is one year by default.
It will also make a new service available, `IAuditLogCleaner`, which provides a utility method to manually clean up the audit logs, if the worker option does not work for you.

## Installing the module

Use the ABP `add-package` command to add the .NET reference in your project and register its module.

```bash
abp add-package Simphonis.Abp.AuditLogs
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
| Simphonis.AuditLogs.MaxDeleteCount | 10000 | Maximum number of audit log rows that can be removed in a single worker run. This setting exists to avoid overloading the database if there are a very large number of old rows to delete. Note that this value cannot be set above 1000000. |

# Known issues

This library was designed to work on single-database applications. If your application uses one separate database per tenant, it will currently only clean the host tenant's database.