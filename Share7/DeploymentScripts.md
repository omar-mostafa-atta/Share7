# Deployment script configuration

The FTP scripts accept explicit parameters or read `SHARE7_FTP_USERNAME` and
`SHARE7_FTP_PASSWORD` from the environment. They fail before connecting when the password
is absent. Existing host/path parameters remain editable.

`Migrate-DatabaseToMonsterASP.ps1` accepts `-TargetConnectionString` or reads
`SHARE7_MIGRATION_TARGET_CONNECTION_STRING`. This migration copies data and clears the
target tables; review the source and target before running it.

Credentials belong in the deployment environment or a private secret store. Never commit
passwords, production connection strings, publish archives or local dependency folders.
Existing local defaults were preserved in the ignored `deployment-credentials.local.json`;
it is private configuration and must remain untracked.

Publishing code to Git does not run these scripts or apply database migrations.
