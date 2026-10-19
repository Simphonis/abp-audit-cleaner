# Simphonis.Abp.AuditLogs

This library contains tools whose purpose is to facilitate or automate the cleanup of the ABP audit log.

# Usage

The library contains an ABP module, which can be added to any solution.
By adding it, a new worker will be registered, to run every hour (not configurable for now). When this worker runs, it deletes all audit log entries whose age exceeds a configured value, which is one year by default.
It will also make a new service available, `IAuditLogCleaner`, which provides a utility method to manually clean up the audit logs.

## Installing the module

1. Add the `Simphonis.Abp.AuditLogs` NuGet package to your solution. We recommend adding it to the Application layer. And if you have several of them, use only one so that the worker does not run twice!
1. In the module of the project where you added the library, don't forget to add a reference to the module:

```cs
[DependsOn(typeof(SimphoAuditLogsModule))]
public class MyModule
{
  // ...
  // No changes needed here!
}
```

## Configuration
A number of settings are available in the form of ABP Settings. They influence the behavior of the scheduled task and have no impact on the direct use of `IAuditLogCleaner`.

You can change the default value of these settings in the application configuration. For example:
```js
{
  // ...
  "Settings" : {
    "Simphonis.AuditLogs.AuditLogRetentionPeriod": "100"
  },
  // ...
}
```

If you prefer to change the value on the C# side, the Setting names are in constants of the `SimphonisAuditLogSettings` class.

**Maximum age of entries**
This value is stored in the ABP Setting `Simphonis.AuditLogs.AuditLogRetentionPeriod`, and contains the maximum age of the audit log, in days. The default value is 365 days.
The scheduled task will attempt to delete all audit log entries older than this configured duration.

**Maximum number of deletable rows**
This value is stored in the ABP Setting `Simphonis.AuditLogs.MaxDeleteCount`, and contains the maximum number of rows to delete on each run of the scheduled task. By default, this limit is 10000 rows. Its value cannot be greater than 1000000.
This setting exists (and is mandatory!) to avoid deleting too much data at once, which would cause problems for the application:
- Blocking of the application until the deletion is complete
- Depending on the database server configuration, the deletion could make the data files grow in an uncontrolled way and fill up the disk space.
