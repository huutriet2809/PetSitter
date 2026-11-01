using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;
using PetSitter.Data;
using PetSitter.Middlewares;
using PetSitter.Models;
using PetSitter.Repositories;
using PetSitter.Services;
using System;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;

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

// Validate BookingSettings:CompletionDeadlineDays > 0 lúc khởi động
var completionDeadlineDays = builder.Configuration.GetValue<int>("BookingSettings:CompletionDeadlineDays");
if (completionDeadlineDays <= 0)
{
    throw new InvalidOperationException("CRITICAL: Cấu hình 'BookingSettings:CompletionDeadlineDays' phải được thiết lập và lớn hơn 0!");
}

// Validate NotificationSettings:TtlDays > 0 lúc khởi động
var notificationTtlDays = builder.Configuration.GetValue<int>("NotificationSettings:TtlDays", 90);
if (notificationTtlDays <= 0)
{
    throw new InvalidOperationException("CRITICAL: Cấu hình 'NotificationSettings:TtlDays' phải được thiết lập và lớn hơn 0!");
}

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
    var databaseName = builder.Configuration["ConnectionStrings:Database"]
                       ?? builder.Configuration["MongoDb:Database"];

    if (string.IsNullOrWhiteSpace(databaseName) || databaseName.StartsWith("YOUR_"))
    {
        if (builder.Environment.IsDevelopment())
        {
            databaseName = "PetSitterDb";
        }
        else
        {
            throw new InvalidOperationException("CRITICAL: MongoDB Database name is missing in configuration (ConnectionStrings:Database)!");
        }
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
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IWithdrawalService, WithdrawalService>();
builder.Services.AddScoped<IRevenueService, RevenueService>();
builder.Services.AddScoped<IBankStatementParser, BankStatementParser>();
builder.Services.AddScoped<IBankReconciliationService, BankReconciliationService>();

// Email Transactional (Queue + Service + BackgroundWorker)
builder.Services.AddSingleton<IEmailQueue, EmailQueue>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddHostedService<PetSitter.BackgroundServices.EmailBackgroundService>();

// Đăng ký BackgroundService bảo trì định kỳ đơn đặt lịch (Auto-expire, Auto-complete, Overdue reminder, Reconcile)
builder.Services.AddHostedService<PetSitter.BackgroundServices.BookingMaintenanceBackgroundService>();

// Data Protection cho mã hóa PayerInfo & tài khoản ngân hàng (lưu thư mục cố định ngoài repo, đọc từ Security:DataProtectionKeyPath)
var dpKeyDir = builder.Configuration["Security:DataProtectionKeyPath"]
               ?? builder.Configuration["DataProtection:KeyDirectory"];
if (string.IsNullOrWhiteSpace(dpKeyDir))
{
    var commonApp = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
    dpKeyDir = !string.IsNullOrWhiteSpace(commonApp)
        ? System.IO.Path.Combine(commonApp, "PetSitter", "dp_keys")
        : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PetSitterKeys");
}
System.IO.Directory.CreateDirectory(dpKeyDir);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new System.IO.DirectoryInfo(dpKeyDir))
    .SetApplicationName("PetSitterApp");

builder.Services.AddHttpClient();

// Cấu hình Cổng thanh toán (PayOS vs Fake Mock) & Sổ cái (Ledger)
var isDevelopment = builder.Environment.IsDevelopment();
var isLiveMode = builder.Configuration.GetValue<bool>("Payments:LiveMode", false);

// Điều kiện an toàn khởi động hệ thống:
// 1. Môi trường không phải Development mà không bật LiveMode -> cấm khởi động với FakeGateway
// 2. FakePaymentGateway CHỈ được phép đăng ký trên môi trường Development
if (!isDevelopment && !isLiveMode)
{
    throw new InvalidOperationException("Lỗi khởi động bảo mật: FakePaymentGateway bị cấm trên môi trường Production/Staging. Cần bật Payments:LiveMode = true và cấu hình khóa PayOS hợp lệ.");
}

if (isLiveMode)
{
    var clientId = builder.Configuration.GetValue<string>("Payments:PayOs:ClientId") ?? builder.Configuration.GetValue<string>("PayOS:ClientId");
    var apiKey = builder.Configuration.GetValue<string>("Payments:PayOs:ApiKey") ?? builder.Configuration.GetValue<string>("PayOS:ApiKey");
    var checksumKey = builder.Configuration.GetValue<string>("Payments:PayOs:ChecksumKey") ?? builder.Configuration.GetValue<string>("PayOS:ChecksumKey");
    if (string.IsNullOrWhiteSpace(clientId) || clientId.StartsWith("YOUR_") ||
        string.IsNullOrWhiteSpace(apiKey) || apiKey.StartsWith("YOUR_") ||
        string.IsNullOrWhiteSpace(checksumKey) || checksumKey.StartsWith("YOUR_"))
    {
        throw new InvalidOperationException("Lỗi cấu hình PayOS: Payments:LiveMode đang bật nhưng PayOS:ClientId, PayOS:ApiKey hoặc PayOS:ChecksumKey chưa được thiết lập khóa thật.");
    }
    builder.Services.AddScoped<IPaymentGateway, PayOsGateway>();
}
else
{
    if (!isDevelopment)
    {
        throw new InvalidOperationException("Lỗi cấu hình: FakePaymentGateway chỉ được phép sử dụng trong môi trường Development.");
    }
    builder.Services.AddSingleton<IPaymentGateway, FakePaymentGateway>();
}

builder.Services.AddScoped<ILedgerService, LedgerService>();
builder.Services.AddScoped<IPaymentService, PaymentService>();

// Add MemoryCache for short caching (e.g. user active status, CIDR lookups)
builder.Services.AddMemoryCache();

// Configure JWT Authentication & Key Rotation
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var allValidationKeys = PetSitter.Helpers.JwtKeyHelper.GetAllValidationKeys(builder.Configuration);

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
        IssuerSigningKeys = allValidationKeys,
        IssuerSigningKeyResolver = (tokenString, securityToken, kid, validationParameters) =>
        {
            if (!string.IsNullOrEmpty(kid))
            {
                var match = validationParameters.IssuerSigningKeys?.FirstOrDefault(k => k.KeyId == kid);
                if (match != null) return new[] { match };
            }
            return validationParameters.IssuerSigningKeys ?? Enumerable.Empty<SecurityKey>();
        },
        ClockSkew = TimeSpan.FromSeconds(5) // ClockSkew nhỏ theo yêu cầu
    };

    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            var userId = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                      ?? context.Principal?.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;

            if (string.IsNullOrEmpty(userId))
            {
                context.Fail("Token thiếu định danh người dùng.");
                return;
            }

            var cache = context.HttpContext.RequestServices.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
            var cacheKey = $"user_active_status_{userId}";

            if (!cache.TryGetValue(cacheKey, out bool isActive))
            {
                try
                {
                    var dbContext = context.HttpContext.RequestServices.GetRequiredService<PetSitterContext>();
                    var user = await dbContext.Users.Find(u => u.UserId == userId).FirstOrDefaultAsync();

                    if (user == null)
                    {
                        context.Fail("Tài khoản của bạn không tồn tại hoặc đã bị xóa.");
                        return;
                    }

                    isActive = user.IsActive != false;
                    cache.Set(cacheKey, isActive, TimeSpan.FromSeconds(30));
                }
                catch (Exception ex)
                {
                    // Giới hạn tần suất log tối đa 1 log / 10 giây để chống spam/nghẽn I/O khi DB sập
                    const string dbErrorThrottleKey = "last_db_error_log_token_validation";
                    if (!cache.TryGetValue(dbErrorThrottleKey, out _))
                    {
                        var logger = context.HttpContext.RequestServices.GetService<ILogger<Program>>();
                        logger?.LogError(ex, "LỖI BẢO MẬT: Không thể xác thực trạng thái tài khoản {UserId} do lỗi kết nối CSDL tạm thời. Áp dụng chiến lược Fail-Closed (log gộp 1 lần / 10s).", userId);
                        cache.Set(dbErrorThrottleKey, true, TimeSpan.FromSeconds(10));
                    }

                    // Chiến lược Fail-Closed: Từ chối truy cập khi CSDL lỗi để ngăn chặn tài khoản bị khóa xâm nhập
                    context.Fail("Hệ thống tạm thời không thể xác thực tài khoản. Vui lòng thử lại sau.");
                    return;
                }
            }

            if (!isActive)
            {
                context.Fail("Tài khoản của bạn đã bị khóa hoặc ngừng hoạt động.");
            }
        }
    };
});

// Configure Rate Limiting (.NET 8 built-in, không thêm thư viện ngoài)
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();
        }
        else
        {
            context.HttpContext.Response.Headers.RetryAfter = "60";
        }
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsync("{\"message\":\"Quá nhiều yêu cầu. Vui lòng thử lại sau.\"}", token);
    };

    // 1. auth-strict: cấu hình qua appsettings (mặc định 5 request/phút/IP) (Login, Register, Change-Password, Forgot-Password, Reset-Password)
    var authStrictLimit = builder.Configuration.GetValue<int>("RateLimiting:AuthStrictLimit", 5);
    options.AddPolicy("auth-strict", httpContext =>
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown_ip";
        return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = authStrictLimit,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });

    // 2. auth-refresh: 20 request/phút/IP
    options.AddPolicy("auth-refresh", httpContext =>
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown_ip";
        return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 20,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });

    // 3. booking-review: 10 request/phút/user (fallback IP)
    options.AddPolicy("booking-review", httpContext =>
    {
        var partitionKey = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                           ?? httpContext.Connection.RemoteIpAddress?.ToString()
                           ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });

    // 4. payout-strict: 5 request/phút/user (fallback IP) cho các thao tác ví nhạy cảm (rút tiền, đổi ngân hàng)
    options.AddPolicy("payout-strict", httpContext =>
    {
        var partitionKey = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                           ?? httpContext.Connection.RemoteIpAddress?.ToString()
                           ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });

    // 5. csp-report: 30 request/phút/IP để thu thập báo cáo CSP không gây quá tải log
    options.AddPolicy("csp-report", httpContext =>
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown_ip";
        return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 30,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });

    // 6. Global limiter: 100 request/phút/user (fallback IP), ngoại lệ hoàn toàn cho PayOS Webhook
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        if (httpContext.Request.Path.StartsWithSegments("/api/payments/payos/webhook", StringComparison.OrdinalIgnoreCase))
        {
            return RateLimitPartition.GetNoLimiter("webhook");
        }

        var partitionKey = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                           ?? httpContext.Connection.RemoteIpAddress?.ToString()
                           ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 100,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });
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

// Seed MongoDB data
using (var scope = app.Services.CreateScope())
{
    var ctx = scope.ServiceProvider.GetRequiredService<PetSitterContext>();
    var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
    await MongoSeeder.SeedDataAsync(ctx, config);
}

// Global Exception Handler (bắt mọi unhandled exception, không lộ stack trace ra ngoài)
app.UseMiddleware<ExceptionHandlingMiddleware>();

// 1. Security Headers & CSP (Report-Only hoặc Enforce theo cấu hình CspSettings:EnforceMode)
var cspEnforceMode = app.Configuration.GetValue<bool>("CspSettings:EnforceMode");
var cspPolicy = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data: https:; font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'self'; frame-ancestors 'none'; form-action 'self'; report-uri /api/csp-report; report-to csp-endpoint;";

app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    context.Response.Headers.Append("Permissions-Policy", "camera=(), microphone=(), geolocation=()");
    context.Response.Headers.Append("Reporting-Endpoints", "csp-endpoint=\"/api/csp-report\"");

    if (cspEnforceMode)
    {
        context.Response.Headers.Append("Content-Security-Policy", cspPolicy);
    }
    else
    {
        context.Response.Headers.Append("Content-Security-Policy-Report-Only", cspPolicy);
    }

    await next();
});

// 2. ForwardedHeaders: cấu hình an toàn với KnownProxies và KnownNetworks (CIDR), mặc định an toàn xóa sạch nguồn lạ
var knownProxiesConfig = app.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>();
var knownNetworksConfig = app.Configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>();

var hasProxies = knownProxiesConfig != null && knownProxiesConfig.Any(p => !string.IsNullOrWhiteSpace(p));
var hasNetworks = knownNetworksConfig != null && knownNetworksConfig.Any(n => !string.IsNullOrWhiteSpace(n));

if (hasProxies || hasNetworks)
{
    var fOptions = new ForwardedHeadersOptions
    {
        ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
    };

    // Mặc định an toàn: xóa sạch proxy và mạng ngầm định để không tin nguồn lạ
    fOptions.KnownProxies.Clear();
    fOptions.KnownNetworks.Clear();

    if (knownProxiesConfig != null)
    {
        foreach (var proxy in knownProxiesConfig)
        {
            if (System.Net.IPAddress.TryParse(proxy?.Trim(), out var ip))
                fOptions.KnownProxies.Add(ip);
        }
    }

    if (knownNetworksConfig != null)
    {
        var parsedNetworks = PetSitter.Helpers.CidrNetworkHelper.ParseCidrNetworks(knownNetworksConfig, app.Logger);
        foreach (var net in parsedNetworks)
        {
            fOptions.KnownNetworks.Add(net);
        }
    }

    app.UseForwardedHeaders(fOptions);
}
else if (!app.Environment.IsDevelopment())
{
    app.Logger.LogWarning("CẢNH BÁO BẢO MẬT: Môi trường Production/Non-Development chưa cấu hình ForwardedHeaders (KnownProxies / KnownNetworks). Middleware không được bật để tránh giả mạo IP.");
}

// 3. HSTS & HttpsRedirection (chỉ ở Production) / Swagger (chỉ ở Development)
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

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

// 4. Static Files (đặt trước RateLimiter để file tĩnh không bị trừ hạn mức 100/phút)
app.UseDefaultFiles();
app.UseStaticFiles();

// 5. Routing
app.UseRouting();

// 6. CORS
app.UseCors("CorsPolicy");

// 7. Authentication
app.UseAuthentication();

// 8. Rate Limiter (đặt SAU UseAuthentication để nhận diện ClaimTypes.NameIdentifier và TRƯỚC UseAuthorization để chặn 429 sớm)
app.UseRateLimiter();

// 9. Authorization
app.UseAuthorization();

// 10. Map Controllers & Pages
app.MapControllers();
app.MapRazorPages();

app.Run();
