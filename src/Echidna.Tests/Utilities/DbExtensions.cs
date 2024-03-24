using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;
using Oracle.ManagedDataAccess.Client;
using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Runtime.InteropServices;

namespace Medallion.Data.Tests;

internal static class DbExtensions
{
    private static readonly ConcurrentDictionary<Db, string> ConnectionStringCache = new();

    public static string ConnectionString(this Db db) => ConnectionStringCache.GetOrAdd(
        db,
        static db =>
        {
            var credentialDirectory = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "credentials"));
            var dataSource = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? null : "localhost";

            switch (db)
            {
                case Db.SqlServer:
                    return TestHelper.IsOnCIPipeline
                        ? new SqlConnectionStringBuilder { DataSource = dataSource ?? @"(local)\SQL2017", UserID = "sa", Password = "Password12!", TrustServerCertificate = true }.ConnectionString
                        : new SqlConnectionStringBuilder { DataSource = dataSource ?? @".\SQLEXPRESS", IntegratedSecurity = true, TrustServerCertificate = true }.ConnectionString;
                case Db.SystemDataSqlServer:
                    return TestHelper.IsOnCIPipeline
                        ? new SqlConnectionStringBuilder { DataSource = dataSource ?? @"(local)\SQL2017", UserID = "sa", Password = "Password12!" }.ConnectionString
                        : new SqlConnectionStringBuilder { DataSource = dataSource ?? @".\SQLEXPRESS", IntegratedSecurity = true }.ConnectionString;
                case Db.Postgres:
                    {
                        var (username, password) = ReadCredentials(TestHelper.IsOnCIPipeline ? "postgres.ci.txt" : "postgres.txt");
                        return new NpgsqlConnectionStringBuilder { Port = 5432, Host = "localhost", Database = "postgres", Username = username, Password = password }.ConnectionString;
                    }
                case Db.MySql:
                    {
                        var (username, password) = ReadCredentials(TestHelper.IsOnCIPipeline ? "mysql.ci.txt" : "mysql.txt");
                        return new MySqlConnectionStringBuilder { Port = 3306, Server = "localhost", Database = "mysql", UserID = username, Password = password }.ConnectionString;
                    }
                case Db.MariaDb:
                    {
                        var (username, password) = ReadCredentials("mysql.txt");
                        return new MySqlConnectionStringBuilder { Port = 3307, Server = "localhost", Database = "mysql", UserID = username, Password = password }.ConnectionString;
                    }
                case Db.Oracle:
                    {
                        var walletDirectory = Directory.GetDirectories(credentialDirectory, "Wallet_*").Single();
                        if (OracleConfiguration.TnsAdmin != walletDirectory)
                        {
                            // directory containing tnsnames.ora and sqlnet.ora
                            OracleConfiguration.TnsAdmin = walletDirectory;
                        }
                        if (OracleConfiguration.WalletLocation != walletDirectory)
                        {
                            // directory containing cwallet.sso
                            OracleConfiguration.WalletLocation = walletDirectory;
                        }
                        var credentials = File.ReadAllLines(Path.Combine(credentialDirectory, "oracle.txt"));
                        if (credentials.Length != 3) { throw new FormatException("must contain exactly 3 lines of text"); }
                        return new OracleConnectionStringBuilder { DataSource = credentials[0], UserID = credentials[1], Password = credentials[2], }.ConnectionString;
                    }
                default:
                    throw Invariant.ShouldNeverGetHere(db.ToString());
            }

            (string username, string password) ReadCredentials(string filename)
            {
                var file = Path.Combine(credentialDirectory, filename);
                var lines = File.ReadAllLines(file);
                if (lines.Length != 2) { throw new FormatException($"{file} must contain exactly 2 lines of text"); }
                return (lines[0], lines[1]);
            }
        }
    );

    public static DbProviderFactory DbProviderFactory(this Db db) => db switch
    {
        Db.SqlServer => SqlClientFactory.Instance,
        Db.SystemDataSqlServer => System.Data.SqlClient.SqlClientFactory.Instance,
        Db.Postgres => NpgsqlFactory.Instance,
        Db.MySql => MySqlConnectorFactory.Instance,
        Db.MariaDb => MySqlConnectorFactory.Instance,
        Db.Oracle => OracleClientFactory.Instance,
        _ => throw Invariant.ShouldNeverGetHere(db.ToString())
    };

    public static Scope<DbDataReader> Read(this Db db, string sql, CommandBehavior behavior = CommandBehavior.SequentialAccess)
    {
        var connection = db.DbProviderFactory().CreateConnection()!;
        connection.ConnectionString = db.ConnectionString();
        connection.Open();

        var command = connection.CreateCommand();
        if (db == Db.Oracle && !sql.Contains("FROM", StringComparison.OrdinalIgnoreCase))
        {
            sql += " FROM sys.dual";
        }
        command.CommandText = sql;

        var reader = command.ExecuteReader();

        return new Scope<DbDataReader>(
            reader,
            () =>
            {
                command.Dispose();
                connection.Dispose();
            }
        );
    }
}
