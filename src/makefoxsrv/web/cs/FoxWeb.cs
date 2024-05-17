using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.HttpOverrides;

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
                ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
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
        });
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

        //app.UseHttpsRedirection();

        app.Use(async (context, next) =>
        {
            context.Response.Headers.Add("Content-Security-Policy", "frame-ancestors *");
            await next();
        });

        app.UseStaticFiles();

        app.UseRouting();

        app.UseSession();

        app.UseForwardedHeaders();

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
