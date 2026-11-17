namespace NIJMITRA.API.BackgroundJobs;

public static class JobRegistration
{
    public static void Register(IServiceCollection services, IConfiguration config)
    {
        if (!config.GetValue("BackgroundJobs:Enabled", true)) return;
    }
}
