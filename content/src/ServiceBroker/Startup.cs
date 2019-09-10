using System;
using Axoom.Extensions.Prometheus.Standalone;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyVendor.ServiceBroker.Broker;
using MyVendor.ServiceBroker.Infrastructure;

namespace MyVendor.ServiceBroker
{
    [UsedImplicitly]
    public class Startup : IStartup
    {
        private readonly IConfiguration _configuration;

        public Startup(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        // Register services for DI
        public IServiceProvider ConfigureServices(IServiceCollection services)
        {
            services.AddPrometheusServer(_configuration.GetSection("Metrics"))
                    .AddSecurity(_configuration.GetSection("Authentication"))
                    .AddRestApi();

            string dbConnectionString = _configuration.GetConnectionString("Database");
            services.AddDbContext<DbContext>(options =>
            {
                if (dbConnectionString.Contains("Host=")) options.UseNpgsql(dbConnectionString);
                else options.UseSqlite(dbConnectionString);
            });

            services.AddHealthChecks()
                    .AddDbContextCheck<DbContext>();

            services.AddBroker(_configuration.GetSection("Broker"));

            return services.BuildServiceProvider();
        }

        // Configure HTTP request pipeline
        public void Configure(IApplicationBuilder app)
            => app.UseHealthChecks("/health")
                  .UseRestApi();

        // Tasks that need to run before serving HTTP requests
        public static void Init(IServiceProvider provider)
        {
            // TODO: Replace .EnsureCreated() with .Migrate() once you start using EF Migrations
            provider.GetRequiredService<DbContext>().Database.EnsureCreated();
        }
    }
}
