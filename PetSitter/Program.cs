using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;
using PetSitter.Data;
using PetSitter.Middlewares;
using PetSitter.Models;
using PetSitter.Repositories;
using PetSitter.Services;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? new[] { "http://localhost:5138" };
builder.Services.AddCors(options =>
{
    options.AddPolicy("CorsPolicy", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});
builder.Services.AddRazorPages(); // Add Razor Pages for FE

// Validate MongoDB Connection String ở mọi môi trường
var mongoDbString = builder.Configuration.GetConnectionString("MongoDb");
if (string.IsNullOrWhiteSpace(mongoDbString) || mongoDbString.StartsWith("YOUR_"))
{
    throw new InvalidOperationException("Lỗi cấu hình: Chuỗi kết nối 'ConnectionStrings:MongoDb' bị rỗng hoặc đang dùng giá trị mẫu. Vui lòng thiết lập bằng lệnh: dotnet user-secrets set \"ConnectionStrings:MongoDb\" \"<chuoi_ket_noi>\"");
}

// Configure MongoDB
builder.Services.AddSingleton<IMongoClient>(sp =>
    new MongoClient(mongoDbString));

builder.Services.AddScoped<IMongoDatabase>(sp =>
{
    var client = sp.GetRequiredService<IMongoClient>();
    var databaseName = builder.Configuration.GetConnectionString("Database")
                       ?? builder.Configuration.GetValue<string>("ConnectionStrings:Database");
    if (string.IsNullOrWhiteSpace(databaseName))
    {
        throw new InvalidOperationException("CRITICAL: MongoDB Database name is missing in configuration (ConnectionStrings:Database)!");
    }
    return client.GetDatabase(databaseName);
});

builder.Services.AddScoped<PetSitterContext>();

// Configure Dependency Injection for Repositories and Services
builder.Services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<ISitterService, PetSitter.Services.SitterService>();
builder.Services.AddScoped<IBookingService, BookingService>();
builder.Services.AddScoped<IPetService, PetService>();

// Configure JWT Authentication
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var secretKey = jwtSettings.GetValue<string>("Secret");

if (string.IsNullOrWhiteSpace(secretKey) || secretKey.StartsWith("YOUR_") || secretKey.Length < 32)
{
    throw new InvalidOperationException("Lỗi cấu hình: Khóa bí mật 'JwtSettings:Secret' bị rỗng, đang dùng giá trị mẫu hoặc ngắn hơn 32 ký tự. Vui lòng thiết lập bằng lệnh: dotnet user-secrets set \"JwtSettings:Secret\" \"<khoa_bi_mat_toi_thieu_32_ky_tu>\"");
}

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings.GetValue<string>("Issuer"),
        ValidAudience = jwtSettings.GetValue<string>("Audience"),
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey!))
    };
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "JWT Authorization header using the Bearer scheme."
    });
    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new string[] {}
        }
    });
});

var app = builder.Build();

// Global Exception Handler
app.UseMiddleware<ExceptionHandlingMiddleware>();

// Seed MongoDB data
using (var scope = app.Services.CreateScope())
{
    var ctx = scope.ServiceProvider.GetRequiredService<PetSitterContext>();
    var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
    await MongoSeeder.SeedDataAsync(ctx, config);
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHttpsRedirection();
}

app.UseCors("CorsPolicy");

app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    await next();
});

app.UseStatusCodePages(async statusCodeContext =>
{
    var request = statusCodeContext.HttpContext.Request;
    var response = statusCodeContext.HttpContext.Response;
    if (response.StatusCode == 404 && !request.Path.StartsWithSegments("/api"))
    {
        response.ContentType = "text/html";
        await response.SendFileAsync(Path.Combine(app.Environment.WebRootPath, "404.html"));
    }
});

app.UseDefaultFiles(); // Support default files like index.html
app.UseStaticFiles(); // Serve static files (CSS/JS)

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapRazorPages(); // Map Razor Pages routes

app.Run();
