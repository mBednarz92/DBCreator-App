using System;
using Microsoft.Data.SqlClient;
using System.Threading;

namespace DatabaseCreator
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.Write("Enter the database name: ");
            string dbName = Console.ReadLine();

            string masterConnectionString = "Data Source=localhost;Initial Catalog=master;Trusted_Connection=True;TrustServerCertificate=True;";

            try
            {
                using (SqlConnection connection = new SqlConnection(masterConnectionString))
                {
                    connection.Open();

                    if (!DatabaseExists(connection, dbName))
                    {
                        Console.WriteLine("Database does not exist. Creating database...");
                        var (dataFilePath, logFilePath) = GetDefaultPaths(connection);
                        string createDatabaseScript = GetDatabaseCreationScript(dbName, dataFilePath, logFilePath);
                        ExecuteScript(connection, createDatabaseScript);

                        if (!WaitForDatabaseAvailability(dbName))
                        {
                            throw new Exception($"Database '{dbName}' did not become available in the expected time.");
                        }

                        Console.WriteLine("Database created successfully.");
                    }
                    else
                    {
                        Console.WriteLine("Database already exists. Skipping creation.");
                    }
                }

                string dbConnectionString = $"Data Source=localhost;Initial Catalog={dbName};Trusted_Connection=True;TrustServerCertificate=True;";
                using (SqlConnection dbConnection = new SqlConnection(dbConnectionString))
                {
                    dbConnection.Open();
                    string createTablesScript = GetTableCreationScript();
                    ExecuteScript(dbConnection, createTablesScript);
                    Console.WriteLine($"Tables created successfully in {dbName} database. Press any key to exit.");
                    Console.Read();
                }
            }
            catch (SqlException ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
                Console.Read();
            }
        }

        static bool DatabaseExists(SqlConnection connection, string dbName)
        {
            string checkDbQuery = $"SELECT COUNT(*) FROM sys.databases WHERE name = '{dbName}'";
            using (SqlCommand command = new SqlCommand(checkDbQuery, connection))
            {
                int count = (int)command.ExecuteScalar();
                return count > 0;
            }
        }

        static bool WaitForDatabaseAvailability(string dbName, int timeoutSeconds = 30)
        {
            int retries = timeoutSeconds;
            string dbConnectionString = $"Data Source=localhost;Initial Catalog={dbName};Trusted_Connection=True;TrustServerCertificate=True;";

            while (retries > 0)
            {
                try
                {
                    using (SqlConnection dbConnection = new SqlConnection(dbConnectionString))
                    {
                        dbConnection.Open();
                        return true;
                    }
                }
                catch (SqlException)
                {
                    Thread.Sleep(1000);
                    retries--;
                }
            }

            return false;
        }

        static (string dataFilePath, string logFilePath) GetDefaultPaths(SqlConnection connection)
        {
            string dataPathQuery = "SELECT SERVERPROPERTY('InstanceDefaultDataPath') AS DataPath, SERVERPROPERTY('InstanceDefaultLogPath') AS LogPath;";
            using (SqlCommand command = new SqlCommand(dataPathQuery, connection))
            using (SqlDataReader reader = command.ExecuteReader())
            {
                if (reader.Read())
                {
                    string dataFilePath = reader["DataPath"].ToString();
                    string logFilePath = reader["LogPath"].ToString();
                    return (dataFilePath, logFilePath);
                }
            }
            throw new InvalidOperationException("Unable to retrieve default data and log file paths.");
        }

        static void ExecuteScript(SqlConnection connection, string script)
        {
            using (SqlCommand command = new SqlCommand(script, connection))
            {
                command.ExecuteNonQuery();
                Console.WriteLine("Script executed.");
            }
        }

        static string GetDatabaseCreationScript(string dbName, string dataFilePath, string logFilePath)
        {
            return $@"
                IF NOT EXISTS (SELECT * FROM sys.server_principals WHERE name = N'handshake')
                BEGIN
                    CREATE LOGIN [handshake] WITH PASSWORD = 'YourSecurePassword', CHECK_POLICY = OFF;
                END;

                CREATE DATABASE [{dbName}]
                CONTAINMENT = NONE
                ON PRIMARY 
                ( NAME = N'{dbName}', FILENAME = N'{dataFilePath}\\{dbName}.mdf', SIZE = 512000KB , MAXSIZE = UNLIMITED, FILEGROWTH = 65536KB )
                LOG ON 
                ( NAME = N'{dbName}_log', FILENAME = N'{logFilePath}\\{dbName}_log.ldf', SIZE = 83904KB , MAXSIZE = 2048GB , FILEGROWTH = 65536KB );

                ALTER DATABASE [{dbName}] SET COMPATIBILITY_LEVEL = 130;
            ";
        }

        static string GetTableCreationScript()
        {
            return $@"
                CREATE USER [handshake] FOR LOGIN [handshake] WITH DEFAULT_SCHEMA=[dbo];
                ALTER ROLE [db_owner] ADD MEMBER [handshake];

                SET ANSI_NULLS ON;
                SET QUOTED_IDENTIFIER ON;

                CREATE TABLE [dbo].[NotificationRequestDataLinks](
                    [NotificationRequestId] INT NOT NULL,
                    [TimbersSyncRequestDataId] INT NOT NULL,
                    CONSTRAINT [PK_NotificationRequestDataLinks] PRIMARY KEY CLUSTERED 
                    (
                        [NotificationRequestId] ASC,
                        [TimbersSyncRequestDataId] ASC
                    ) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
                ) ON [PRIMARY];

                CREATE TABLE [dbo].[NotificationRequests](
                    [Id] INT IDENTITY(1,1) NOT NULL,
                    [Timestamp] DATETIME NULL,
                    [Body] NVARCHAR(MAX) NULL,
                    CONSTRAINT [PK_NotificationRequests] PRIMARY KEY CLUSTERED 
                    (
                        [Id] ASC
                    ) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
                ) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY];

                CREATE TABLE [dbo].[RequestDataStorage](
                    [Id] INT IDENTITY(1,1) NOT NULL,
                    [RequestDataJson] NVARCHAR(MAX) NULL,
                    [CreatedAt] DATETIME NULL DEFAULT (getdate()),
                    [DataType] VARCHAR(255) NULL,
                    [status] VARCHAR(50) NULL,
                    PRIMARY KEY CLUSTERED 
                    (
                        [Id] ASC
                    ) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
                ) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY];

                CREATE TABLE [dbo].[TimbersSyncRequestData](
                    [RequestId] INT IDENTITY(1,1) NOT NULL,
                    [Id] NVARCHAR(255) NULL,
                    [ExternalId] NVARCHAR(255) NULL,
                    [RequestNumber] INT NULL,
                    [Type] NVARCHAR(255) NULL,
                    [Action] NVARCHAR(255) NULL,
                    [ExtraData_Barcode] NVARCHAR(255) NULL,
                    [Status] NVARCHAR(50) NULL,
                    CONSTRAINT [PK_TimbersSyncRequestData] PRIMARY KEY CLUSTERED 
                    (
                        [RequestId] ASC
                    ) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
                ) ON [PRIMARY];

                CREATE NONCLUSTERED INDEX [IX_NotificationRequestDataLinks_TimbersSyncRequestDataId] ON [dbo].[NotificationRequestDataLinks]
                (
                    [TimbersSyncRequestDataId] ASC
                ) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY];

                ALTER TABLE [dbo].[NotificationRequestDataLinks] WITH CHECK ADD CONSTRAINT [FK_NotificationRequestDataLinks_NotificationRequests_NotificationRequestId] 
                FOREIGN KEY([NotificationRequestId]) REFERENCES [dbo].[NotificationRequests]([Id]) ON DELETE CASCADE;

                ALTER TABLE [dbo].[NotificationRequestDataLinks] CHECK CONSTRAINT [FK_NotificationRequestDataLinks_NotificationRequests_NotificationRequestId];

                ALTER TABLE [dbo].[NotificationRequestDataLinks] WITH CHECK ADD CONSTRAINT [FK_NotificationRequestDataLinks_TimbersSyncRequestData_TimbersSyncRequestDataId] 
                FOREIGN KEY([TimbersSyncRequestDataId]) REFERENCES [dbo].[TimbersSyncRequestData]([RequestId]) ON DELETE CASCADE;

                ALTER TABLE [dbo].[NotificationRequestDataLinks] CHECK CONSTRAINT [FK_NotificationRequestDataLinks_TimbersSyncRequestData_TimbersSyncRequestDataId];
            ";
        }
    }
}
