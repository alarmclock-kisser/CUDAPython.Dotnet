
using System.Text.Json.Serialization;
using CUDAPython.Dotnet.Api.Middleware;
using CUDAPython.Dotnet.Api.Services;
using CUDAPython.Dotnet.Python;
using Scalar.AspNetCore;

namespace CUDAPython.Dotnet.Api
{
    /// <summary>Configures and runs the CUDA Python REST API.</summary>
    public class Program
    {
        /// <summary>Starts the web application.</summary>
        /// <param name="args">Command-line arguments.</param>
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddControllers()
                .AddJsonOptions(options =>
                    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
            builder.Services.AddOpenApi();
            builder.Services.AddProblemDetails();
            builder.Services.AddExceptionHandler<ApiExceptionHandler>();
            builder.Services.AddScoped<AdminApiKeyAuthorizationFilter>();
            builder.Services.AddSingleton(_ => CudaPythonRuntime.Initialize());
            builder.Services.AddSingleton<IPythonEnvironmentService, PythonEnvironmentService>();
            builder.Services.AddSingleton<ICudaPythonService, CudaPythonService>();
            builder.Services.AddSingleton<IMaintenanceService, MaintenanceService>();
            builder.Services.AddSingleton<PythonEnvironmentPromotion>();

            var app = builder.Build();
            app.Services.GetRequiredService<PythonEnvironmentPromotion>().PromotePendingUpdate();

            app.UseExceptionHandler();
            app.UseStatusCodePages();
            app.UseHttpsRedirection();

            if (app.Environment.IsDevelopment())
            {
                app.MapOpenApi();
                app.MapScalarApiReference("/swagger", options => options.WithTitle("CUDA Python API"));
            }

            app.MapControllers();

            app.Run();
        }
    }
}
