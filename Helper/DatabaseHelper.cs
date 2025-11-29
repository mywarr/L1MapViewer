using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using MySql.Data.MySqlClient;
using L1FlyMapViewer;

namespace L1MapViewer.Helper
{
    public static class DatabaseHelper
    {
        private static MySqlConnection? currentConnection;
        private static string? connectionString;
        private static string jsonFilePath = Path.Combine(Path.GetTempPath(), "mapviewer_db_connections.json");
        private static string lastUsedFilePath = Path.Combine(Path.GetTempPath(), "mapviewer_last_connection.txt");

        // 連線狀態屬性
        public static bool IsConnected
        {
            get
            {
                return currentConnection != null &&
                       currentConnection.State == System.Data.ConnectionState.Open;
            }
        }

        // 取得目前的連線
        public static MySqlConnection GetConnection()
        {
            if (!IsConnected || currentConnection == null)
            {
                throw new InvalidOperationException("資料庫未連線");
            }
            return currentConnection;
        }

        // 連線到資料庫
        public static bool Connect(string server, string port, string database, string username, string password)
        {
            try
            {
                // 關閉現有連線
                Disconnect();

                // 建立連線字串
                connectionString = $"Server={server};Port={port};Database={database};Uid={username};Pwd={password};";

                // 建立新連線
                currentConnection = new MySqlConnection(connectionString);
                currentConnection.Open();

                return true;
            }
            catch (Exception ex)
            {
                currentConnection = null;
                throw new Exception("連線失敗: " + ex.Message, ex);
            }
        }

        // 斷線
        public static void Disconnect()
        {
            if (currentConnection != null)
            {
                try
                {
                    if (currentConnection.State == System.Data.ConnectionState.Open)
                    {
                        currentConnection.Close();
                    }
                    currentConnection.Dispose();
                }
                catch { }
                finally
                {
                    currentConnection = null;
                    connectionString = null;
                }
            }
        }

        // 儲存多組連線設定
        public static void SaveMultipleConnectionSettings(List<DatabaseConnection> connections)
        {
            try
            {
                // 加密密碼
                var connectionsToSave = new List<DatabaseConnection>();
                foreach (var conn in connections)
                {
                    connectionsToSave.Add(new DatabaseConnection
                    {
                        Name = conn.Name,
                        Server = conn.Server,
                        Port = conn.Port,
                        Database = conn.Database,
                        Username = conn.Username,
                        Password = EncodePassword(conn.Password)
                    });
                }

                // 序列化為 JSON
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(connectionsToSave, options);

                // 寫入檔案
                File.WriteAllText(jsonFilePath, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                throw new Exception("儲存連線設定失敗: " + ex.Message, ex);
            }
        }

        // 載入多組連線設定
        public static List<DatabaseConnection> LoadMultipleConnectionSettings()
        {
            var connections = new List<DatabaseConnection>();

            if (!File.Exists(jsonFilePath))
            {
                return connections;
            }

            try
            {
                // 讀取 JSON 檔案
                string json = File.ReadAllText(jsonFilePath, Encoding.UTF8);
                var loadedConnections = JsonSerializer.Deserialize<List<DatabaseConnection>>(json);

                if (loadedConnections != null)
                {
                    // 解密密碼
                    foreach (var conn in loadedConnections)
                    {
                        conn.Password = DecodePassword(conn.Password);
                        connections.Add(conn);
                    }
                }
            }
            catch
            {
                // 如果載入失敗，返回空列表
            }

            return connections;
        }

        // 執行 SQL 查詢（返回 MySqlDataReader）
        public static MySqlDataReader ExecuteQuery(string query)
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("資料庫未連線");
            }

            MySqlCommand command = new MySqlCommand(query, currentConnection);
            return command.ExecuteReader();
        }

        // 執行 SQL 命令（INSERT, UPDATE, DELETE）
        public static int ExecuteNonQuery(string query)
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("資料庫未連線");
            }

            MySqlCommand command = new MySqlCommand(query, currentConnection);
            return command.ExecuteNonQuery();
        }

        // 執行 SQL 命令並返回單一值
        public static object ExecuteScalar(string query)
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("資料庫未連線");
            }

            MySqlCommand command = new MySqlCommand(query, currentConnection);
            return command.ExecuteScalar();
        }

        // 測試連線
        public static bool TestConnection(string server, string port, string database, string username, string password)
        {
            try
            {
                string testConnectionString = $"Server={server};Port={port};Database={database};Uid={username};Pwd={password};";
                using (MySqlConnection testConnection = new MySqlConnection(testConnectionString))
                {
                    testConnection.Open();
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        // 加密密碼（簡單的 Base64 編碼）
        private static string EncodePassword(string password)
        {
            if (string.IsNullOrEmpty(password))
                return "";

            try
            {
                return Convert.ToBase64String(Encoding.UTF8.GetBytes(password));
            }
            catch
            {
                return password;
            }
        }

        // 解密密碼
        private static string DecodePassword(string encodedPassword)
        {
            if (string.IsNullOrEmpty(encodedPassword))
                return "";

            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(encodedPassword));
            }
            catch
            {
                return encodedPassword;
            }
        }

        // 保存最後使用的連線名稱
        public static void SaveLastUsedConnection(string connectionName)
        {
            try
            {
                File.WriteAllText(lastUsedFilePath, connectionName, Encoding.UTF8);
            }
            catch
            {
                // 忽略錯誤
            }
        }

        // 讀取最後使用的連線名稱
        public static string? LoadLastUsedConnection()
        {
            try
            {
                if (File.Exists(lastUsedFilePath))
                {
                    return File.ReadAllText(lastUsedFilePath, Encoding.UTF8);
                }
            }
            catch
            {
                // 忽略錯誤
            }
            return null;
        }

        // 自動連線到最後使用的連線
        public static bool AutoConnectToLastUsed()
        {
            try
            {
                string lastConnectionName = LoadLastUsedConnection();
                if (string.IsNullOrEmpty(lastConnectionName))
                    return false;

                var connections = LoadMultipleConnectionSettings();
                var lastConnection = connections.Find(c => c.Name == lastConnectionName);

                if (lastConnection != null)
                {
                    Connect(lastConnection.Server, lastConnection.Port, lastConnection.Database,
                           lastConnection.Username, lastConnection.Password);
                    return true;
                }
            }
            catch
            {
                // 連線失敗，忽略
            }
            return false;
        }
    }
}
