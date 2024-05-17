using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

public class FoxWeb
{
    public static IHostBuilder CreateHostBuilder(string[] args, int port) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseStartup<FoxWeb>()
                          .UseUrls($"http://*:{port}");
            });

    public void ConfigureServices(IServiceCollection services)
    {
        Console.WriteLine("Configuring services...");
        services.AddRazorPages();
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders =
                ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
        });

        services.AddServerSideBlazor();
        services.AddSignalR();
        services.AddHttpContextAccessor();
        services.AddDistributedMemoryCache();
        services.AddSession(options =>
        {
            options.IdleTimeout = TimeSpan.FromDays(30);
            options.Cookie.HttpOnly = true;
            options.Cookie.IsEssential = true;
            options.Cookie.Domain = "admin.makefox.bot";
            options.Cookie.SameSite = SameSiteMode.None;  // Ensure the cookie is available cross-site
        });
        services.AddSingleton<IHttpContextService, HttpContextService>(); // Register the HttpContextService
        Console.WriteLine("Services configured.");
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env, IHubContext<makefoxsrv.ChatHub> hubContext)
    {
        Console.WriteLine("Configuring application...");

        if (env.IsDevelopment())
        {
            Console.WriteLine("Development environment detected. Using Developer Exception Page.");
            app.UseDeveloperExceptionPage();
        }
        else
        {
            Console.WriteLine("Production environment detected. Using Exception Handler and HSTS.");
            app.UseExceptionHandler("/Error");
            app.UseHsts();
        }

        app.UseForwardedHeaders(); // Ensure this is early in the pipeline
        app.UseSession(); // Ensure this is before routing

        app.Use(async (context, next) =>
        {
            context.Response.Headers.Add("Content-Security-Policy", "frame-ancestors *");
            await next();
        });

        app.UseStaticFiles();

        app.UseRouting();

        app.UseMiddleware<HttpContextMiddleware>(); // Use the HttpContextMiddleware

        makefoxsrv.DatabaseHandler.Initialize(hubContext);

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapBlazorHub();
            Console.WriteLine("Mapped Blazor Hub.");

            endpoints.MapHub<makefoxsrv.ChatHub>("/chathub");
            Console.WriteLine("Mapped SignalR Chat Hub.");

            endpoints.MapFallbackToPage("/_Host");

            endpoints.MapControllers(); // Enable API controllers
        });

        Console.WriteLine("Application configured.");
    }

    public static void StartWebServer(int port)
    {
        Console.WriteLine($"Starting web server on port {port}...");
        CreateHostBuilder(new string[0], port).Build().Run();
        Console.WriteLine("Web server started.");
    }
}

// HttpContextService to store HttpContext
public interface IHttpContextService
{
    HttpContext GetCurrentHttpContext();
    void SetCurrentHttpContext(HttpContext httpContext);
}

public class HttpContextService : IHttpContextService
{
    private static AsyncLocal<HttpContext> _currentHttpContext = new AsyncLocal<HttpContext>();

    public HttpContext GetCurrentHttpContext()
    {
        return _currentHttpContext.Value;
    }

    public void SetCurrentHttpContext(HttpContext httpContext)
    {
        _currentHttpContext.Value = httpContext;
    }
}

// Middleware to set HttpContext
public class HttpContextMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IHttpContextService _httpContextService;

    public HttpContextMiddleware(RequestDelegate next, IHttpContextService httpContextService)
    {
        _next = next;
        _httpContextService = httpContextService;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        _httpContextService.SetCurrentHttpContext(context);
        await _next(context);
    }
}
