# MF-02 monitoring SQL proof

This opt-in tool validates the production V6 monitoring path against the configured SQL database without retaining fixture data. It prefers a saved LIVE camera setup and uses RECORDED only when no LIVE candidate exists. The JSON result makes that fallback explicit.

It creates a temporary Zone, ACTIVE configuration, rule and mapping inside one outer `SERIALIZABLE` transaction; sends deterministic aggregate observations through the production snapshot reader, evaluator, writer and EF store; verifies warning open, CRITICAL escalation, recovery clear and no Incident creation; then always rolls back.

The selected camera must already have a successful connection test. The tool temporarily changes camera/connection state only inside the rollback transaction. It does not capture video, call the AI service, modify schema or create a persistent demo fixture.

From the backend repository root:

```powershell
dotnet run --project tools/Mf02MonitoringSqlProof/Mf02MonitoringSqlProof.csproj -c Release
```

By default it reads `src/Supermarket.Api/appsettings.Local.json`. Set `MF02_LOCAL_SETTINGS` to an alternate local settings path. Never commit that settings file or its credentials.
