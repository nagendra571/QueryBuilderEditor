using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace QueryBuilder.Api.Seed;

/// <summary>
/// Provisions a small "sales" sample schema (tables + business-logic views) in the demo
/// source database so the app has something real to browse and query out of the box.
/// This stands in for the developer-authored SQL views the product is designed around.
/// </summary>
public sealed class DemoSourceSeeder(IConfiguration configuration, ILogger<DemoSourceSeeder> logger)
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString("DemoSource");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        await EnsureDatabaseExistsAsync(connectionString, cancellationToken);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        if (await SchemaAlreadySeededAsync(connection, cancellationToken))
        {
            return;
        }

        logger.LogInformation("Seeding demo 'sales' sample schema into the source database...");

        foreach (var statement in SchemaStatements)
        {
            await ExecuteAsync(connection, statement, cancellationToken);
        }

        foreach (var statement in DataStatements)
        {
            await ExecuteAsync(connection, statement, cancellationToken);
        }

        foreach (var statement in ViewStatements)
        {
            await ExecuteAsync(connection, statement, cancellationToken);
        }

        logger.LogInformation("Demo sample schema seeded.");
    }

    private static async Task EnsureDatabaseExistsAsync(string connectionString, CancellationToken cancellationToken)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        var databaseName = builder.InitialCatalog;
        builder.InitialCatalog = "master";

        await using var masterConnection = new SqlConnection(builder.ConnectionString);
        await masterConnection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(
            $"IF DB_ID('{databaseName}') IS NULL CREATE DATABASE [{databaseName}];", masterConnection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> SchemaAlreadySeededAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("SELECT OBJECT_ID('sales.Customers')", connection);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is not null and not DBNull;
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 60 };
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static readonly string[] SchemaStatements =
    [
        "CREATE SCHEMA sales;",

        """
        CREATE TABLE sales.Customers (
            CustomerId INT IDENTITY(1,1) PRIMARY KEY,
            CustomerName NVARCHAR(200) NOT NULL,
            Email NVARCHAR(256) NOT NULL,
            Country NVARCHAR(100) NOT NULL,
            IsActive BIT NOT NULL DEFAULT(1),
            CreatedAtUtc DATETIME2 NOT NULL DEFAULT(SYSUTCDATETIME())
        );
        """,

        """
        CREATE TABLE sales.Products (
            ProductId INT IDENTITY(1,1) PRIMARY KEY,
            ProductName NVARCHAR(200) NOT NULL,
            Category NVARCHAR(100) NOT NULL,
            UnitPrice DECIMAL(10,2) NOT NULL,
            IsDiscontinued BIT NOT NULL DEFAULT(0)
        );
        """,

        """
        CREATE TABLE sales.Orders (
            OrderId INT IDENTITY(1,1) PRIMARY KEY,
            CustomerId INT NOT NULL FOREIGN KEY REFERENCES sales.Customers(CustomerId),
            OrderDate DATE NOT NULL,
            Status NVARCHAR(50) NOT NULL,
            ShippingCountry NVARCHAR(100) NOT NULL
        );
        """,

        """
        CREATE TABLE sales.OrderItems (
            OrderItemId INT IDENTITY(1,1) PRIMARY KEY,
            OrderId INT NOT NULL FOREIGN KEY REFERENCES sales.Orders(OrderId),
            ProductId INT NOT NULL FOREIGN KEY REFERENCES sales.Products(ProductId),
            Quantity INT NOT NULL,
            UnitPrice DECIMAL(10,2) NOT NULL
        );
        """
    ];

    private static readonly string[] DataStatements =
    [
        """
        INSERT INTO sales.Customers (CustomerName, Email, Country, IsActive) VALUES
        (N'Contoso Retail', N'ap@contoso.com', N'United States', 1),
        (N'Northwind Traders', N'orders@northwind.com', N'United States', 1),
        (N'Fabrikam Inc', N'purchasing@fabrikam.com', N'Canada', 1),
        (N'Adventure Works', N'ap@adventure-works.com', N'United Kingdom', 1),
        (N'Tailspin Toys', N'buyers@tailspin.com', N'Australia', 0),
        (N'Wide World Importers', N'orders@wideworld.com', N'India', 1);
        """,

        """
        INSERT INTO sales.Products (ProductName, Category, UnitPrice, IsDiscontinued) VALUES
        (N'Wireless Mouse', N'Accessories', 24.99, 0),
        (N'Mechanical Keyboard', N'Accessories', 89.50, 0),
        (N'27" 4K Monitor', N'Displays', 349.00, 0),
        (N'USB-C Dock', N'Accessories', 129.00, 0),
        (N'Laptop Stand', N'Accessories', 39.95, 0),
        (N'Noise Cancelling Headset', N'Audio', 199.00, 0),
        (N'Webcam 1080p', N'Peripherals', 59.99, 1),
        (N'Ergonomic Chair', N'Furniture', 459.00, 0);
        """,

        """
        INSERT INTO sales.Orders (CustomerId, OrderDate, Status, ShippingCountry) VALUES
        (1, '2025-01-14', N'Shipped', N'United States'),
        (1, '2025-03-02', N'Delivered', N'United States'),
        (2, '2025-02-10', N'Delivered', N'United States'),
        (2, '2025-05-21', N'Processing', N'United States'),
        (3, '2025-01-30', N'Delivered', N'Canada'),
        (3, '2025-06-11', N'Shipped', N'Canada'),
        (4, '2025-04-04', N'Delivered', N'United Kingdom'),
        (4, '2025-07-19', N'Cancelled', N'United Kingdom'),
        (5, '2024-12-05', N'Delivered', N'Australia'),
        (6, '2025-02-27', N'Delivered', N'India'),
        (6, '2025-06-30', N'Processing', N'India'),
        (1, '2025-08-08', N'Shipped', N'United States'),
        (2, '2025-08-15', N'Delivered', N'United States'),
        (3, '2025-09-01', N'Processing', N'Canada'),
        (4, '2025-09-10', N'Shipped', N'United Kingdom');
        """,

        """
        INSERT INTO sales.OrderItems (OrderId, ProductId, Quantity, UnitPrice) VALUES
        (1, 1, 2, 24.99), (1, 2, 1, 89.50),
        (2, 3, 1, 349.00),
        (3, 4, 1, 129.00), (3, 5, 2, 39.95),
        (4, 6, 1, 199.00),
        (5, 1, 3, 24.99),
        (6, 3, 2, 349.00), (6, 8, 1, 459.00),
        (7, 2, 1, 89.50), (7, 4, 1, 129.00),
        (8, 6, 1, 199.00),
        (9, 7, 2, 59.99),
        (10, 1, 5, 24.99),
        (11, 5, 1, 39.95), (11, 2, 1, 89.50),
        (12, 8, 1, 459.00),
        (13, 3, 1, 349.00), (13, 6, 1, 199.00),
        (14, 4, 2, 129.00),
        (15, 1, 1, 24.99), (15, 2, 1, 89.50), (15, 5, 1, 39.95);
        """
    ];

    private static readonly string[] ViewStatements =
    [
        """
        CREATE VIEW sales.vw_OrderDetails AS
        SELECT
            o.OrderId,
            o.OrderDate,
            o.Status,
            o.ShippingCountry,
            c.CustomerId,
            c.CustomerName,
            c.Email AS CustomerEmail,
            c.IsActive AS IsActiveCustomer,
            p.ProductId,
            p.ProductName,
            p.Category,
            oi.Quantity,
            oi.UnitPrice,
            CAST(oi.Quantity * oi.UnitPrice AS DECIMAL(12,2)) AS LineTotal
        FROM sales.Orders o
        JOIN sales.Customers c ON c.CustomerId = o.CustomerId
        JOIN sales.OrderItems oi ON oi.OrderId = o.OrderId
        JOIN sales.Products p ON p.ProductId = oi.ProductId;
        """,

        """
        CREATE VIEW sales.vw_CustomerOrderSummary AS
        SELECT
            c.CustomerId,
            c.CustomerName,
            c.Country,
            c.IsActive,
            COUNT(DISTINCT o.OrderId) AS OrderCount,
            CAST(ISNULL(SUM(oi.Quantity * oi.UnitPrice), 0) AS DECIMAL(12,2)) AS LifetimeValue,
            MAX(o.OrderDate) AS LastOrderDate
        FROM sales.Customers c
        LEFT JOIN sales.Orders o ON o.CustomerId = c.CustomerId
        LEFT JOIN sales.OrderItems oi ON oi.OrderId = o.OrderId
        GROUP BY c.CustomerId, c.CustomerName, c.Country, c.IsActive;
        """
    ];
}
