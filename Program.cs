using ElasticSearchPdfApi.Database;
using ElasticSearchPdfApi.Models;
using ElasticSearchPdfApi.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Nest;
using Serilog;
using Serilog.Sinks.Elasticsearch;

var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Elasticsearch(
        new ElasticsearchSinkOptions(new Uri(builder.Configuration["ElasticsearchSettings:Uri"]))
        {
            AutoRegisterTemplate = true,
            IndexFormat =
                $"{builder.Configuration["ApplicationName"]}-logs-{DateTime.UtcNow:yyyy-MM}",
            NumberOfShards = 2,
            NumberOfReplicas = 1,
        }
    )
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Database"))
);

builder
    .Services.AddIdentityCore<User>()
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddApiEndpoints()
    .AddDefaultTokenProviders();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

builder.Services.AddAuthorization();
builder.Services.AddAuthentication().AddCookie(IdentityConstants.ApplicationScheme);

// builder.Services.AddCors(o =>
//     o.AddPolicy(
//         "MyPolicy",
//         b =>
//         {
//             b.WithOrigins("http://localhost:5173")
//                 .AllowAnyMethod()
//                 .AllowAnyHeader()
//                 .AllowCredentials();
//         }
//     )
// );

var elasticsearchUrl =
    builder.Configuration["ElasticsearchSettings:Url"] ?? "http://localhost:9200";
var settings = new ConnectionSettings(new Uri(elasticsearchUrl)).DefaultIndex("pdf_documents");

builder.Services.AddSingleton<IElasticClient>(new ElasticClient(settings));
builder.Services.AddSingleton<ElasticsearchService>();

var uploadDirectory =
    builder.Configuration["PdfSettings:UploadDirectory"]
    ?? Path.Combine(Directory.GetCurrentDirectory(), "Uploads");
builder.Services.AddSingleton<PdfProcessingService>(provider => new PdfProcessingService(
    uploadDirectory,
    provider.GetRequiredService<ElasticsearchService>()
));

var app = builder.Build();

// app.UseCors("MyPolicy");

app.UseAuthorization();
app.MapControllers();
app.UseStaticFiles();
app.MapGet("/", () => Results.Redirect("/index.html"));

app.MapIdentityApi<User>();

await SeedRolesAndAdmin(app.Services);
app.Run();

static async Task SeedRolesAndAdmin(IServiceProvider serviceProvider)
{
    using (var scope = serviceProvider.CreateScope())
    {
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

        string[] roleNames = { "Admin", "User", "Editor", "Internal" };
        foreach (var roleName in roleNames)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                await roleManager.CreateAsync(new IdentityRole(roleName));
            }
        }

        var adminEmail = "admin@example.com";
        var adminUser = await userManager.FindByEmailAsync(adminEmail);

        if (adminUser == null)
        {
            var newAdmin = new User
            {
                UserName = adminEmail,
                Email = adminEmail,
                EmailConfirmed = true,
            };

            var result = await userManager.CreateAsync(newAdmin, "Admin@123");
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(newAdmin, "Admin");
            }
        }
    }
}
