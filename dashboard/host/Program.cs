using Aspire.Dashboard;

var builder = WebApplication.CreateBuilder(args);

// Dashboard settings are grouped by domain. The host owns every value; the
// dashboard library only consumes them through the fluent builders.
builder.AddDashboard(dashboard => dashboard
    .Branding(branding => branding
        .Title(builder.Configuration["Dashboard:Branding:Title"]
            ?? builder.Configuration["ASPIRE_DASHBOARD_APPLICATION_NAME"]
            ?? "РА-Монитор")
        .LogoUrl(builder.Configuration["Dashboard:Branding:LogoUrl"] ?? "/img/logo-ra.svg"))
    .Hosting(hosting => hosting
        .BasePath(builder.Configuration["Dashboard:BasePath"] ?? "/")
        .PublicUrl(builder.Configuration["Dashboard:Frontend:PublicUrl"])
        .EndpointUrls(builder.Configuration["ASPNETCORE_URLS"]))
    .Files(files => files
        .Logs(
            builder.Configuration["Dashboard:Files:Logs:Path"],
            builder.Configuration["Dashboard:Files:Logs:Title"])
        .Share(
            builder.Configuration["Dashboard:Files:Share:Path"],
            builder.Configuration["Dashboard:Files:Share:Title"]))
    .Otlp(otlp => otlp
        .GrpcEndpoint(builder.Configuration["ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL"])
        .HttpEndpoint(builder.Configuration["ASPIRE_DASHBOARD_OTLP_HTTP_ENDPOINT_URL"])
        .SuppressUnsecuredMessage(builder.Configuration.GetValue(
            "Dashboard:Otlp:SuppressUnsecuredMessage", true)))
    .Management(management => management
        .Timeout(builder.Configuration.GetValue("Management:TimeoutSeconds", 10)))
    .Security(security => security
        .AllowAnonymous(builder.Configuration.GetValue(
            "ASPIRE_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS", true))));

var app = builder.Build();

app.UseDashboard();

app.Run();
