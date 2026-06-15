using System.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Data.SqlClient;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(options =>
{
    options.AddPolicy("DevClient", policy =>
        policy.WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
              .AllowAnyHeader()
              .AllowAnyMethod());
});

builder.Services.AddSingleton<SqlConnectionFactory>();
builder.Services.AddSingleton<JwtTokenService>();

var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key));

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = signingKey,
            ClockSkew = TimeSpan.FromMinutes(2)
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseCors("DevClient");
}

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

var api = app.MapGroup("/api");

api.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTimeOffset.Now }));

api.MapPost("/auth/login", async (LoginRequest request, SqlConnectionFactory db, JwtTokenService tokens) =>
{
    await using var connection = db.Create();
    var account = await connection.QuerySingleOrDefaultAsync<AccountRow>(
        """
        SELECT AccountId, Username, PasswordHash, PasswordSalt, Role, ReaderCardNo, IsEnabled
        FROM dbo.Accounts
        WHERE Username = @Username
        """,
        new { request.Username });

    if (account is null || !account.IsEnabled || !PasswordService.Verify(request.Username, request.Password, account.PasswordSalt, account.PasswordHash))
    {
        return Results.Unauthorized();
    }

    var token = tokens.Create(account);
    return Results.Ok(new LoginResponse(token, account.Username, account.Role, account.ReaderCardNo));
});

api.MapGet("/auth/me", (ClaimsPrincipal user) =>
{
    return Results.Ok(new
    {
        username = user.Identity?.Name,
        role = user.FindFirstValue(ClaimTypes.Role),
        readerCardNo = user.FindFirstValue("readerCardNo")
    });
}).RequireAuthorization();

var books = api.MapGroup("/books").RequireAuthorization();

books.MapGet("", async (string? q, SqlConnectionFactory db) =>
{
    await using var connection = db.Create();
    var rows = await connection.QueryAsync<BookDto>(
        """
        SELECT Isbn, Title, Publisher, Author, TotalCopies, AvailableCopies, IsBorrowable
        FROM dbo.Books
        WHERE @Q IS NULL
           OR Title LIKE N'%' + @Q + N'%'
           OR Author LIKE N'%' + @Q + N'%'
           OR Isbn LIKE N'%' + @Q + N'%'
        ORDER BY Title
        """,
        new { Q = string.IsNullOrWhiteSpace(q) ? null : q.Trim() });

    return Results.Ok(rows);
});

books.MapGet("/{isbn}", async (string isbn, SqlConnectionFactory db) =>
{
    await using var connection = db.Create();
    var row = await connection.QuerySingleOrDefaultAsync<BookDto>(
        """
        SELECT Isbn, Title, Publisher, Author, TotalCopies, AvailableCopies, IsBorrowable
        FROM dbo.Books
        WHERE Isbn = @isbn
        """,
        new { isbn });

    return row is null ? Results.NotFound() : Results.Ok(row);
});

books.MapPost("", async (UpsertBookRequest request, SqlConnectionFactory db) =>
{
    if (request.TotalCopies < 0 || request.AvailableCopies < 0 || request.AvailableCopies > request.TotalCopies)
    {
        return Results.BadRequest(new { message = "馆藏数量和可借数量不合法。" });
    }

    await using var connection = db.Create();
    await connection.ExecuteAsync(
        """
        INSERT INTO dbo.Books (Isbn, Title, Publisher, Author, TotalCopies, AvailableCopies, IsBorrowable)
        VALUES (@Isbn, @Title, @Publisher, @Author, @TotalCopies, @AvailableCopies, @IsBorrowable)
        """,
        request);

    return Results.Created($"/api/books/{request.Isbn}", request);
}).RequireAuthorization("AdminOnly");

books.MapPut("/{isbn}", async (string isbn, UpsertBookRequest request, SqlConnectionFactory db) =>
{
    if (request.TotalCopies < 0 || request.AvailableCopies < 0 || request.AvailableCopies > request.TotalCopies)
    {
        return Results.BadRequest(new { message = "馆藏数量和可借数量不合法。" });
    }

    await using var connection = db.Create();
    var affected = await connection.ExecuteAsync(
        """
        UPDATE dbo.Books
           SET Title = @Title,
               Publisher = @Publisher,
               Author = @Author,
               TotalCopies = @TotalCopies,
               AvailableCopies = @AvailableCopies,
               IsBorrowable = @IsBorrowable,
               UpdatedAt = SYSUTCDATETIME()
         WHERE Isbn = @isbn
        """,
        new { isbn, request.Title, request.Publisher, request.Author, request.TotalCopies, request.AvailableCopies, request.IsBorrowable });

    return affected == 0 ? Results.NotFound() : Results.NoContent();
}).RequireAuthorization("AdminOnly");

books.MapDelete("/{isbn}", async (string isbn, SqlConnectionFactory db) =>
{
    await using var connection = db.Create();
    var loanCount = await connection.ExecuteScalarAsync<int>(
        "SELECT COUNT(1) FROM dbo.BorrowRecords WHERE Isbn = @isbn",
        new { isbn });

    if (loanCount > 0)
    {
        return Results.Conflict(new { message = "该图书存在借阅记录，不能删除。" });
    }

    var affected = await connection.ExecuteAsync("DELETE FROM dbo.Books WHERE Isbn = @isbn", new { isbn });
    return affected == 0 ? Results.NotFound() : Results.NoContent();
}).RequireAuthorization("AdminOnly");

var readers = api.MapGroup("/readers").RequireAuthorization();

readers.MapGet("", async (string? q, SqlConnectionFactory db) =>
{
    await using var connection = db.Create();
    var rows = await connection.QueryAsync<ReaderDto>(
        """
        SELECT
            r.ReaderCardNo, r.Name, r.Gender, r.Title, r.MaxBorrowCount, r.BorrowedCount, r.Department, r.Phone,
            CAST(ISNULL(SUM(CASE WHEN br.ReturnDate IS NOT NULL AND br.Fine > 0 AND br.FinePaid = 0 THEN br.Fine ELSE 0 END), 0) AS DECIMAL(10,2)) AS UnpaidFine
        FROM dbo.Readers r
        LEFT JOIN dbo.BorrowRecords br ON br.ReaderCardNo = r.ReaderCardNo
        WHERE @Q IS NULL
           OR r.ReaderCardNo LIKE N'%' + @Q + N'%'
           OR r.Name LIKE N'%' + @Q + N'%'
           OR r.Department LIKE N'%' + @Q + N'%'
        GROUP BY r.ReaderCardNo, r.Name, r.Gender, r.Title, r.MaxBorrowCount, r.BorrowedCount, r.Department, r.Phone
        ORDER BY r.ReaderCardNo
        """,
        new { Q = string.IsNullOrWhiteSpace(q) ? null : q.Trim() });

    return Results.Ok(rows);
}).RequireAuthorization("AdminOnly");

readers.MapGet("/{cardNo}", async (string cardNo, ClaimsPrincipal user, SqlConnectionFactory db) =>
{
    if (!CanAccessReader(user, cardNo))
    {
        return Results.Forbid();
    }

    await using var connection = db.Create();
    var reader = await connection.QuerySingleOrDefaultAsync<ReaderDto>(
        """
        SELECT
            r.ReaderCardNo, r.Name, r.Gender, r.Title, r.MaxBorrowCount, r.BorrowedCount, r.Department, r.Phone,
            CAST(ISNULL(SUM(CASE WHEN br.ReturnDate IS NOT NULL AND br.Fine > 0 AND br.FinePaid = 0 THEN br.Fine ELSE 0 END), 0) AS DECIMAL(10,2)) AS UnpaidFine
        FROM dbo.Readers r
        LEFT JOIN dbo.BorrowRecords br ON br.ReaderCardNo = r.ReaderCardNo
        WHERE r.ReaderCardNo = @cardNo
        GROUP BY r.ReaderCardNo, r.Name, r.Gender, r.Title, r.MaxBorrowCount, r.BorrowedCount, r.Department, r.Phone
        """,
        new { cardNo });

    if (reader is null)
    {
        return Results.NotFound();
    }

    var openLoans = await connection.QueryAsync<BorrowRecordDto>(
        SqlText.BorrowRecordSelect + " WHERE br.ReaderCardNo = @cardNo AND br.ReturnDate IS NULL ORDER BY br.BorrowDate DESC",
        new { cardNo });

    return Results.Ok(new { reader, openLoans });
});

readers.MapPost("", async (UpsertReaderRequest request, SqlConnectionFactory db) =>
{
    await using var connection = db.Create();
    await connection.ExecuteAsync(
        """
        INSERT INTO dbo.Readers (ReaderCardNo, Name, Gender, Title, MaxBorrowCount, BorrowedCount, Department, Phone)
        VALUES (@ReaderCardNo, @Name, @Gender, @Title, @MaxBorrowCount, @BorrowedCount, @Department, @Phone)
        """,
        request);

    return Results.Created($"/api/readers/{request.ReaderCardNo}", request);
}).RequireAuthorization("AdminOnly");

readers.MapPut("/{cardNo}", async (string cardNo, UpsertReaderRequest request, SqlConnectionFactory db) =>
{
    await using var connection = db.Create();
    var affected = await connection.ExecuteAsync(
        """
        UPDATE dbo.Readers
           SET Name = @Name,
               Gender = @Gender,
               Title = @Title,
               MaxBorrowCount = @MaxBorrowCount,
               BorrowedCount = @BorrowedCount,
               Department = @Department,
               Phone = @Phone,
               UpdatedAt = SYSUTCDATETIME()
         WHERE ReaderCardNo = @cardNo
        """,
        new { cardNo, request.Name, request.Gender, request.Title, request.MaxBorrowCount, request.BorrowedCount, request.Department, request.Phone });

    return affected == 0 ? Results.NotFound() : Results.NoContent();
}).RequireAuthorization("AdminOnly");

readers.MapDelete("/{cardNo}", async (string cardNo, SqlConnectionFactory db) =>
{
    await using var connection = db.Create();
    var loanCount = await connection.ExecuteScalarAsync<int>(
        "SELECT COUNT(1) FROM dbo.BorrowRecords WHERE ReaderCardNo = @cardNo",
        new { cardNo });

    if (loanCount > 0)
    {
        return Results.Conflict(new { message = "该读者存在借阅记录，不能删除。" });
    }

    await connection.ExecuteAsync("DELETE FROM dbo.Accounts WHERE ReaderCardNo = @cardNo", new { cardNo });
    var affected = await connection.ExecuteAsync("DELETE FROM dbo.Readers WHERE ReaderCardNo = @cardNo", new { cardNo });
    return affected == 0 ? Results.NotFound() : Results.NoContent();
}).RequireAuthorization("AdminOnly");

readers.MapPost("/{cardNo}/pay-fine", async (string cardNo, SqlConnectionFactory db) =>
{
    await using var connection = db.Create();
    var paidRecords = await connection.ExecuteScalarAsync<int>(
        "dbo.sp_PayReaderFine",
        new { ReaderCardNo = cardNo },
        commandType: CommandType.StoredProcedure);

    return Results.Ok(new { paidRecords });
}).RequireAuthorization("AdminOnly");

var accounts = api.MapGroup("/accounts").RequireAuthorization("AdminOnly");

accounts.MapGet("", async (SqlConnectionFactory db) =>
{
    await using var connection = db.Create();
    var rows = await connection.QueryAsync<AccountDto>(
        """
        SELECT AccountId, Username, Role, ReaderCardNo, IsEnabled
        FROM dbo.Accounts
        ORDER BY Role, Username
        """);
    return Results.Ok(rows);
});

accounts.MapPost("", async (UpsertAccountRequest request, SqlConnectionFactory db) =>
{
    if (string.IsNullOrWhiteSpace(request.Password))
    {
        return Results.BadRequest(new { message = "新增账号必须填写密码。" });
    }

    var salt = "LIBRARY_SYSTEM_2026";
    var hash = PasswordService.Hash(request.Username, request.Password, salt);
    await using var connection = db.Create();
    var id = await connection.ExecuteScalarAsync<int>(
        """
        INSERT INTO dbo.Accounts (Username, PasswordHash, PasswordSalt, Role, ReaderCardNo, IsEnabled)
        OUTPUT INSERTED.AccountId
        VALUES (@Username, @hash, @salt, @Role, @ReaderCardNo, @IsEnabled)
        """,
        new { request.Username, hash, salt, request.Role, request.ReaderCardNo, request.IsEnabled });
    return Results.Created($"/api/accounts/{id}", new { accountId = id });
});

accounts.MapPut("/{accountId:int}", async (int accountId, UpsertAccountRequest request, SqlConnectionFactory db) =>
{
    await using var connection = db.Create();
    if (string.IsNullOrWhiteSpace(request.Password))
    {
        var affectedWithoutPassword = await connection.ExecuteAsync(
            """
            UPDATE dbo.Accounts
               SET Username = @Username,
                   Role = @Role,
                   ReaderCardNo = @ReaderCardNo,
                   IsEnabled = @IsEnabled
             WHERE AccountId = @accountId
            """,
            new { accountId, request.Username, request.Role, request.ReaderCardNo, request.IsEnabled });
        return affectedWithoutPassword == 0 ? Results.NotFound() : Results.NoContent();
    }

    var salt = "LIBRARY_SYSTEM_2026";
    var hash = PasswordService.Hash(request.Username, request.Password, salt);
    var affected = await connection.ExecuteAsync(
        """
        UPDATE dbo.Accounts
           SET Username = @Username,
               PasswordHash = @hash,
               PasswordSalt = @salt,
               Role = @Role,
               ReaderCardNo = @ReaderCardNo,
               IsEnabled = @IsEnabled
         WHERE AccountId = @accountId
        """,
        new { accountId, request.Username, hash, salt, request.Role, request.ReaderCardNo, request.IsEnabled });
    return affected == 0 ? Results.NotFound() : Results.NoContent();
});

accounts.MapDelete("/{accountId:int}", async (int accountId, SqlConnectionFactory db) =>
{
    await using var connection = db.Create();
    var affected = await connection.ExecuteAsync("DELETE FROM dbo.Accounts WHERE AccountId = @accountId", new { accountId });
    return affected == 0 ? Results.NotFound() : Results.NoContent();
});

var loans = api.MapGroup("/borrow-records").RequireAuthorization();

loans.MapGet("", async (string? q, string? status, ClaimsPrincipal user, SqlConnectionFactory db) =>
{
    await using var connection = db.Create();
    var sql = new StringBuilder(SqlText.BorrowRecordSelect);
    sql.AppendLine();
    sql.Append(
        """
        WHERE (@Q IS NULL OR b.Title LIKE N'%' + @Q + N'%' OR br.Isbn LIKE N'%' + @Q + N'%' OR r.Name LIKE N'%' + @Q + N'%' OR r.ReaderCardNo LIKE N'%' + @Q + N'%')
          AND (@Status IS NULL
               OR (@Status = N'open' AND br.ReturnDate IS NULL)
               OR (@Status = N'returned' AND br.ReturnDate IS NOT NULL)
               OR (@Status = N'overdue' AND br.ReturnDate IS NULL AND DATEADD(DAY, br.LoanDays, br.BorrowDate) < CAST(GETDATE() AS DATE)))
        """);

    var readerCardNo = user.FindFirstValue("readerCardNo");
    if (!user.IsInRole("Admin"))
    {
        sql.Append(" AND br.ReaderCardNo = @ReaderCardNo");
    }

    sql.Append(" ORDER BY br.BorrowDate DESC, br.LoanId DESC");

    var rows = await connection.QueryAsync<BorrowRecordDto>(
        sql.ToString(),
        new
        {
            Q = string.IsNullOrWhiteSpace(q) ? null : q.Trim(),
            Status = string.IsNullOrWhiteSpace(status) ? null : status.Trim(),
            ReaderCardNo = readerCardNo
        });

    return Results.Ok(rows);
});

loans.MapPost("", async (UpsertBorrowRecordRequest request, SqlConnectionFactory db) =>
{
    await using var connection = db.Create();

    var overdueCount = await connection.ExecuteScalarAsync<int>(@"
        SELECT COUNT(1) 
        FROM dbo.BorrowRecords 
        WHERE ReaderCardNo = @ReaderCardNo 
          AND ReturnDate IS NULL 
          AND DATEADD(day, LoanDays, BorrowDate) < GETDATE()", 
        new { ReaderCardNo = request.ReaderCardNo });

    if (overdueCount > 0)
    {
        return Results.BadRequest(new { message = "借阅失败：该读者当前有逾期未归还的图书，请先归还！" });
    }
    
    var id = await connection.ExecuteScalarAsync<int>(
        """
        INSERT INTO dbo.BorrowRecords (ReaderCardNo, Isbn, BorrowDate, LoanDays, ReturnDate, Fine, FinePaid, Remark)
        OUTPUT INSERTED.LoanId
        VALUES (@ReaderCardNo, @Isbn, @BorrowDate, @LoanDays, @ReturnDate, @Fine, @FinePaid, @Remark)
        """,
        request);
    return Results.Created($"/api/borrow-records/{id}", new { loanId = id });
}).RequireAuthorization("AdminOnly");

loans.MapPut("/{loanId:int}", async (int loanId, UpsertBorrowRecordRequest request, SqlConnectionFactory db) =>
{
    await using var connection = db.Create();
    var affected = await connection.ExecuteAsync(
        """
        UPDATE dbo.BorrowRecords
           SET ReaderCardNo = @ReaderCardNo,
               Isbn = @Isbn,
               BorrowDate = @BorrowDate,
               LoanDays = @LoanDays,
               ReturnDate = @ReturnDate,
               Fine = @Fine,
               FinePaid = @FinePaid,
               Remark = @Remark
         WHERE LoanId = @loanId
        """,
        new { loanId, request.ReaderCardNo, request.Isbn, request.BorrowDate, request.LoanDays, request.ReturnDate, request.Fine, request.FinePaid, request.Remark });
    return affected == 0 ? Results.NotFound() : Results.NoContent();
}).RequireAuthorization("AdminOnly");

loans.MapDelete("/{loanId:int}", async (int loanId, SqlConnectionFactory db) =>
{
    await using var connection = db.Create();
    var affected = await connection.ExecuteAsync("DELETE FROM dbo.BorrowRecords WHERE LoanId = @loanId", new { loanId });
    return affected == 0 ? Results.NotFound() : Results.NoContent();
}).RequireAuthorization("AdminOnly");

loans.MapPost("/borrow", async (BorrowBookRequest request, ClaimsPrincipal user, SqlConnectionFactory db) =>
{
    try
    {
        var readerCardNo = request.ReaderCardNo;
        if (!user.IsInRole("Admin"))
        {
            readerCardNo = user.FindFirstValue("readerCardNo") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(readerCardNo))
            {
                return Results.Forbid();
            }
        }

        if (string.IsNullOrWhiteSpace(readerCardNo))
        {
            return Results.BadRequest(new { message = "请选择读者。" });
        }

        await using var connection = db.Create();

        // ================== 核心漏洞修复：在这里强行拦截逾期读者 ==================
        var overdueCount = await connection.ExecuteScalarAsync<int>(@"
            SELECT COUNT(1) 
            FROM dbo.BorrowRecords 
            WHERE ReaderCardNo = @ReaderCardNo 
              AND ReturnDate IS NULL 
              AND DATEADD(day, LoanDays, BorrowDate) < GETDATE()", 
            new { ReaderCardNo = readerCardNo });

        if (overdueCount > 0)
        {
            return Results.BadRequest(new { message = "借阅失败：您当前有逾期未归还的图书，请先归还！" });
        }
        // ====================================================================

        var loanIdDecimal = await connection.ExecuteScalarAsync<decimal>(
            "dbo.sp_BorrowBook",
            new { ReaderCardNo = readerCardNo, request.Isbn, request.BorrowDate, request.LoanDays },
            commandType: CommandType.StoredProcedure);
        return Results.Ok(new { loanId = Convert.ToInt32(loanIdDecimal) });
    }
    catch (SqlException ex) when (ex.Number >= 51001 && ex.Number <= 51005)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
}).RequireAuthorization();

loans.MapPost("/{loanId:int}/return", async (int loanId, ReturnBookRequest request, SqlConnectionFactory db) =>
{
    try
    {
        await using var connection = db.Create();
        var fine = await connection.ExecuteScalarAsync<decimal>(
            "dbo.sp_ReturnBook",
            new { LoanId = loanId, request.ReturnDate },
            commandType: CommandType.StoredProcedure);
        return Results.Ok(new { fine });
    }
    catch (SqlException ex) when (ex.Number == 51006)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
}).RequireAuthorization("AdminOnly");

var reports = api.MapGroup("/reports").RequireAuthorization();

reports.MapGet("/overdue", async (ClaimsPrincipal user, SqlConnectionFactory db) =>
{
    await using var connection = db.Create();
    var sql =
        """
        SELECT LoanId, Isbn, Title, ReaderName, ReaderCardNo, BorrowDate, DueDate, OverdueDays, EstimatedFine
        FROM dbo.vw_OverdueBorrowRecords
        """;

    if (!user.IsInRole("Admin"))
    {
        sql += " WHERE ReaderCardNo = @ReaderCardNo";
    }

    sql += " ORDER BY OverdueDays DESC";
    var rows = await connection.QueryAsync<OverdueDto>(sql, new { ReaderCardNo = user.FindFirstValue("readerCardNo") });
    return Results.Ok(rows);
});

reports.MapGet("/dashboard", async (ClaimsPrincipal user, SqlConnectionFactory db) =>
{
    await using var connection = db.Create();
    var role = user.FindFirstValue(ClaimTypes.Role);
    var readerCardNo = user.FindFirstValue("readerCardNo");

    if (role == "Reader")
    {
        var readerStats = await connection.QuerySingleAsync(
            """
            SELECT
                (SELECT COUNT(1) FROM dbo.BorrowRecords WHERE ReaderCardNo = @readerCardNo AND ReturnDate IS NULL) AS currentLoans,
                (SELECT COUNT(1) FROM dbo.vw_OverdueBorrowRecords WHERE ReaderCardNo = @readerCardNo) AS overdueLoans,
                (SELECT ISNULL(SUM(Fine), 0) FROM dbo.BorrowRecords WHERE ReaderCardNo = @readerCardNo AND Fine > 0 AND FinePaid = 0) AS unpaidFine,
                (SELECT COUNT(1) FROM dbo.BorrowRecords WHERE ReaderCardNo = @readerCardNo) AS totalLoans
            """,
            new { readerCardNo });

        var readerMonthly = await connection.QueryAsync(
            """
            SELECT FORMAT(BorrowDate, 'yyyy-MM') AS month, COUNT(1) AS count
            FROM dbo.BorrowRecords
            WHERE ReaderCardNo = @readerCardNo
              AND BorrowDate >= DATEADD(MONTH, -6, CAST(GETDATE() AS DATE))
            GROUP BY FORMAT(BorrowDate, 'yyyy-MM')
            ORDER BY month
            """,
            new { readerCardNo });

        return Results.Ok(new { stats = readerStats, monthly = readerMonthly, popular = Array.Empty<object>() });
    }

    var stats = await connection.QuerySingleAsync(
        """
        SELECT
            (SELECT COUNT(1) FROM dbo.Books) AS bookKinds,
            (SELECT ISNULL(SUM(TotalCopies), 0) FROM dbo.Books) AS totalCopies,
            (SELECT ISNULL(SUM(AvailableCopies), 0) FROM dbo.Books) AS availableCopies,
            (SELECT COUNT(1) FROM dbo.BorrowRecords WHERE ReturnDate IS NULL) AS currentLoans,
            (SELECT COUNT(1) FROM dbo.vw_OverdueBorrowRecords) AS overdueLoans,
            (SELECT ISNULL(SUM(Fine), 0) FROM dbo.BorrowRecords WHERE Fine > 0 AND FinePaid = 0) AS unpaidFine
        """);

    var monthly = await connection.QueryAsync(
        """
        SELECT FORMAT(BorrowDate, 'yyyy-MM') AS month, COUNT(1) AS count
        FROM dbo.BorrowRecords
        WHERE BorrowDate >= DATEADD(MONTH, -6, CAST(GETDATE() AS DATE))
        GROUP BY FORMAT(BorrowDate, 'yyyy-MM')
        ORDER BY month
        """);

    var popular = await connection.QueryAsync(
        """
        SELECT TOP 5 b.Title AS title, COUNT(1) AS count
        FROM dbo.BorrowRecords br
        JOIN dbo.Books b ON b.Isbn = br.Isbn
        GROUP BY b.Title
        ORDER BY count DESC
        """);

    return Results.Ok(new { stats, monthly, popular });
});

app.MapFallbackToFile("index.html");
app.Run();

static bool CanAccessReader(ClaimsPrincipal user, string cardNo)
{
    return user.IsInRole("Admin") || string.Equals(user.FindFirstValue("readerCardNo"), cardNo, StringComparison.OrdinalIgnoreCase);
}

public sealed class SqlConnectionFactory(IConfiguration configuration)
{
    private readonly string _connectionString = configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("Missing Default connection string.");

    public SqlConnection Create() => new(_connectionString);
}

public sealed class JwtTokenService(IConfiguration configuration)
{
    private readonly JwtOptions _options = configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();

    public string Create(AccountRow account)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, account.AccountId.ToString()),
            new(ClaimTypes.Name, account.Username),
            new(ClaimTypes.Role, account.Role)
        };

        if (!string.IsNullOrWhiteSpace(account.ReaderCardNo))
        {
            claims.Add(new Claim("readerCardNo", account.ReaderCardNo));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(8),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public static class PasswordService
{
    public static bool Verify(string username, string password, string salt, string expectedHash)
    {
        return string.Equals(Hash(username, password, salt), expectedHash, StringComparison.OrdinalIgnoreCase);
    }

    public static string Hash(string username, string password, string salt)
    {
        var bytes = SHA256.HashData(Encoding.Unicode.GetBytes($"{username}:{password}:{salt}"));
        return Convert.ToHexString(bytes);
    }
}

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "LibrarySystem";
    public string Audience { get; set; } = "LibrarySystemClient";
    public string Key { get; set; } = "LibrarySystemCourseProjectJwtKey_ChangeInProduction_2026";
}

public static class SqlText
{
    public const string BorrowRecordSelect =
        """
        SELECT
            br.LoanId,
            br.ReaderCardNo,
            r.Name AS ReaderName,
            br.Isbn,
            b.Title AS BookTitle,
            br.BorrowDate,
            br.LoanDays,
            DATEADD(DAY, br.LoanDays, br.BorrowDate) AS DueDate,
            br.ReturnDate,
            br.Fine,
            br.FinePaid,
            br.Remark,
            CASE
                WHEN br.ReturnDate IS NOT NULL THEN N'已归还'
                WHEN DATEADD(DAY, br.LoanDays, br.BorrowDate) < CAST(GETDATE() AS DATE) THEN N'逾期'
                ELSE N'借出中'
            END AS Status
        FROM dbo.BorrowRecords br
        JOIN dbo.Readers r ON r.ReaderCardNo = br.ReaderCardNo
        JOIN dbo.Books b ON b.Isbn = br.Isbn
        """;
}

public sealed record LoginRequest(string Username, string Password);
public sealed record LoginResponse(string Token, string Username, string Role, string? ReaderCardNo);
public sealed record AccountRow(int AccountId, string Username, string PasswordHash, string PasswordSalt, string Role, string? ReaderCardNo, bool IsEnabled);
public sealed record AccountDto(int AccountId, string Username, string Role, string? ReaderCardNo, bool IsEnabled);
public sealed record UpsertAccountRequest(string Username, string? Password, string Role, string? ReaderCardNo, bool IsEnabled);
public sealed record BookDto(string Isbn, string Title, string Publisher, string Author, int TotalCopies, int AvailableCopies, bool IsBorrowable);
public sealed record UpsertBookRequest(string Isbn, string Title, string Publisher, string Author, int TotalCopies, int AvailableCopies, bool IsBorrowable);
public sealed record ReaderDto(string ReaderCardNo, string Name, string Gender, string Title, int MaxBorrowCount, int BorrowedCount, string Department, string? Phone, decimal UnpaidFine);
public sealed record UpsertReaderRequest(string ReaderCardNo, string Name, string Gender, string Title, int MaxBorrowCount, int BorrowedCount, string Department, string? Phone);
public sealed record BorrowRecordDto(int LoanId, string ReaderCardNo, string ReaderName, string Isbn, string BookTitle, DateTime BorrowDate, int LoanDays, DateTime DueDate, DateTime? ReturnDate, decimal Fine, bool FinePaid, string? Remark, string Status);
public sealed record UpsertBorrowRecordRequest(string ReaderCardNo, string Isbn, DateTime BorrowDate, int LoanDays, DateTime? ReturnDate, decimal Fine, bool FinePaid, string? Remark);
public sealed record BorrowBookRequest(string ReaderCardNo, string Isbn, DateTime? BorrowDate, int LoanDays);
public sealed record ReturnBookRequest(DateTime? ReturnDate);
public sealed record OverdueDto(int LoanId, string Isbn, string Title, string ReaderName, string ReaderCardNo, DateTime BorrowDate, DateTime DueDate, int OverdueDays, decimal EstimatedFine);
