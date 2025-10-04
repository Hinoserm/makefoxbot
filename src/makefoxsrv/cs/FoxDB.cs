using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using MySqlConnector;

public class DbColumnAttribute : Attribute
{
    public string Name { get; }

    public DbColumnAttribute(string name)
    {
        Name = name;
    }
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class DbIncludeAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true)]
public class DbColumnMappingAttribute : Attribute
{
    public string PropertyPath { get; private set; }
    public string ColumnName { get; private set; }
    public bool ExcludeFromLoading { get; private set; } // New flag

    public DbColumnMappingAttribute(string propertyPath, string columnName, bool excludeFromLoading = false)
    {
        PropertyPath = propertyPath;
        ColumnName = columnName;
        ExcludeFromLoading = excludeFromLoading;
    }
}

namespace makefoxsrv
{
    internal class FoxDB
    {

        public static async Task CheckAndCreatePartitionsAsync()
        {
            try
            {
                using var SQL = new MySqlConnection(FoxMain.sqlConnectionString);
                await SQL.OpenAsync();

                // Find all partitioned tables in this database
                const string findTablesSql = @"
                    SELECT DISTINCT TABLE_NAME
                    FROM information_schema.PARTITIONS
                    WHERE TABLE_SCHEMA = DATABASE()
                      AND PARTITION_NAME IS NOT NULL
                ";

                var tableNames = new List<string>();
                using (var cmd = new MySqlCommand(findTablesSql, SQL))
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                        tableNames.Add(reader.GetString(0));
                }

                var days = new[] { DateTime.Today, DateTime.Today.AddDays(1) };

                foreach (var table in tableNames)
                {
                    foreach (var day in days)
                    {
                        var partitionName = $"p{day:yyyyMMdd}";
                        var cutoffDate = day.AddDays(1).ToString("yyyy-MM-dd");

                        if (!await PartitionExistsAsync(table, partitionName))
                        {
                            var alterSql = $@"
                                ALTER TABLE `{table}` ADD PARTITION (
                                    PARTITION {partitionName}
                                    VALUES LESS THAN (TO_DAYS('{cutoffDate}'))
                                );
                            ";

                            using var alterCmd = new MySqlCommand(alterSql, SQL);
                            await alterCmd.ExecuteNonQueryAsync();

                            FoxLog.WriteLine($"Added partition \"{partitionName}\" to table \"{table}\"", LogLevel.INFO);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                FoxLog.LogException(ex, $"Startup partition maintenance error: {ex.Message}");
                throw;
            }
        }

        [Cron(hours: 1)]
        public static async Task CronBuildTomorrowsPartitionsAsync()
        {
            try
            {
                using var SQL = new MySqlConnection(FoxMain.sqlConnectionString);
                await SQL.OpenAsync();

                const string findTablesSql = @"
                    SELECT DISTINCT TABLE_NAME
                    FROM information_schema.PARTITIONS
                    WHERE TABLE_SCHEMA = DATABASE()
                      AND PARTITION_NAME IS NOT NULL
                ";

                var tableNames = new List<string>();
                using (var cmd = new MySqlCommand(findTablesSql, SQL))
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                        tableNames.Add(reader.GetString(0));
                }

                var tomorrow = DateTime.Today.AddDays(1);
                var partitionName = $"p{tomorrow:yyyyMMdd}";
                var cutoffDate = tomorrow.AddDays(1).ToString("yyyy-MM-dd");

                foreach (var table in tableNames)
                {
                    try
                    {
                        if (!await PartitionExistsAsync(table, partitionName))
                        {
                            var alterSql = $@"
                                ALTER TABLE `{table}` ADD PARTITION (
                                    PARTITION {partitionName}
                                    VALUES LESS THAN (TO_DAYS('{cutoffDate}'))
                                );
                            ";
                            using var alterCmd = new MySqlCommand(alterSql, SQL);
                            await alterCmd.ExecuteNonQueryAsync();

                            FoxLog.WriteLine($"[CRON] Added partition \"{partitionName}\" to table \"{table}\"", LogLevel.INFO);
                        }
                    }
                    catch (Exception tableEx)
                    {
                        FoxLog.LogException(tableEx, $"Partition maintenance failed for table \"{table}\"");
                    }
                }
            }
            catch (Exception ex)
            {
                FoxLog.LogException(ex, $"Cron partition maintenance error: {ex.Message}");
            }
        }

        private static async Task<bool> PartitionExistsAsync(string table, string partitionName)
        {
            try
            {
                using var SQL = new MySqlConnection(FoxMain.sqlConnectionString);
                await SQL.OpenAsync();

                const string checkSql = @"
                    SELECT COUNT(*)
                    FROM information_schema.PARTITIONS
                    WHERE TABLE_SCHEMA = DATABASE()
                      AND TABLE_NAME = @tname
                      AND PARTITION_NAME = @pname;
                ";

                using var checkCmd = new MySqlCommand(checkSql, SQL);
                checkCmd.Parameters.AddWithValue("@tname", table);
                checkCmd.Parameters.AddWithValue("@pname", partitionName);

                var exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());
                return exists > 0;
            }
            catch (Exception ex)
            {
                FoxLog.LogException(ex, $"Partition existence check failed for {table}/{partitionName}: {ex.Message}");
                return false;
            }
        }



        public static async Task SaveObjectAsync<T>(T obj, string tableName)
        {
            using (var connection = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await connection.OpenAsync();
                var command = new MySqlCommand { Connection = connection };
                var insertColumns = new List<string>();
                var insertValues = new List<string>();
                var updateSets = new List<string>();
                var debugParameters = new StringBuilder();

                await ProcessObject(obj, command, insertColumns, insertValues, updateSets, debugParameters, "");

                if (!insertColumns.Any())
                {
                    //Console.WriteLine("No columns to insert or update found.");
                    return;
                }

                if (obj is null)
                    throw new ArgumentNullException(nameof(obj));

                command.CommandText = $"INSERT INTO {tableName} ({string.Join(", ", insertColumns)}) VALUES ({string.Join(", ", insertValues)}) ON DUPLICATE KEY UPDATE {string.Join(", ", updateSets)};";

                //Console.WriteLine($"Executing SQL: {command.CommandText}");
                //Console.WriteLine($"With parameters: {debugParameters}");

                await command.ExecuteNonQueryAsync();

                var lastInsertId = command.LastInsertedId;

                // Find the property or field marked with DbColumn("id")
                var idMember = obj.GetType().GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(m => m.GetCustomAttribute<DbColumnAttribute>()?.Name == "id");

                if (idMember != null)
                {
                    if (idMember.MemberType == MemberTypes.Property)
                    {
                        var propertyInfo = (PropertyInfo)idMember;
                        if (propertyInfo.CanWrite)
                        {
                            SetMemberValue(propertyInfo, propertyInfo.PropertyType, obj, lastInsertId);
                        }
                    }
                    else if (idMember.MemberType == MemberTypes.Field)
                    {
                        var fieldInfo = (FieldInfo)idMember;
                        SetMemberValue(fieldInfo, fieldInfo.FieldType, obj, lastInsertId);
                    }
                }

                void SetMemberValue(MemberInfo memberInfo, Type memberType, object targetObject, long lastInsertIdValue)
                {
                    object? valueToSet = null;

                    if (memberType == typeof(long))
                    {
                        valueToSet = lastInsertIdValue;
                    }
                    else if (memberType == typeof(ulong))
                    {
                        valueToSet = Convert.ToUInt64(lastInsertIdValue);
                    }
                    else if (memberType == typeof(int))
                    {
                        valueToSet = Convert.ToInt32(lastInsertIdValue);
                    }
                    else if (memberType == typeof(uint))
                    {
                        valueToSet = Convert.ToUInt32(lastInsertIdValue);
                    }

                    if (valueToSet != null)
                    {
                        if (memberInfo.MemberType == MemberTypes.Property)
                        {
                            ((PropertyInfo)memberInfo).SetValue(targetObject, valueToSet);
                        }
                        else if (memberInfo.MemberType == MemberTypes.Field)
                        {
                            ((FieldInfo)memberInfo).SetValue(targetObject, valueToSet);
                        }
                    }
                }
            }
        }

        private static async Task ProcessObject(object? obj, MySqlCommand command, List<string> insertColumns, List<string> insertValues, List<string> updateSets, StringBuilder debugParameters, string basePath)
        {
            if (obj is null)
                throw new ArgumentNullException(nameof(obj));

            var members = obj.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                            .Cast<MemberInfo>()
                            .Concat(obj.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance));

            foreach (var member in members)
            {
                var dbColumnAttr = member.GetCustomAttribute<DbColumnAttribute>();
                var dbIncludeAttr = member.GetCustomAttribute<DbIncludeAttribute>();
                object? memberValue = member.MemberType == MemberTypes.Property ? ((PropertyInfo)member).GetValue(obj) : ((FieldInfo)member).GetValue(obj);

                if (dbColumnAttr != null)
                {
                    // Convert bools to 0 or 1, enums to strings, and directly handle DateTime and other types
                    if (memberValue is bool boolVal) memberValue = boolVal ? 1 : 0;
                    else if (memberValue?.GetType().IsEnum == true) memberValue = memberValue.ToString();

                    var paramName = $"@{dbColumnAttr.Name}";
                    if (!command.Parameters.Contains(paramName))
                    {
                        command.Parameters.AddWithValue(paramName, memberValue ?? DBNull.Value);
                        insertColumns.Add(dbColumnAttr.Name);
                        insertValues.Add(paramName);
                        if (!dbColumnAttr.Name.Equals("id", StringComparison.OrdinalIgnoreCase)) // Exclude 'id' if auto-increment
                        {
                            updateSets.Add($"{dbColumnAttr.Name} = VALUES({dbColumnAttr.Name})");
                        }
                        debugParameters.AppendLine($"Column: {dbColumnAttr.Name}, Value: {memberValue}");
                    }
                }
                else if (dbIncludeAttr != null && memberValue != null)
                {
                    // Recursively process objects marked with DbInclude
                    await ProcessObject(memberValue, command, insertColumns, insertValues, updateSets, debugParameters, "");
                }

                // Processing DbColumnMapping attributes
                var mappingAttributes = member.GetCustomAttributes<DbColumnMappingAttribute>(true);
                foreach (var mapping in mappingAttributes)
                {
                    // Resolve nested property path to value
                    object? targetValue = ResolvePropertyPath(obj, mapping.PropertyPath);
                    PrepareForDatabase(mapping.ColumnName, targetValue, command, insertColumns, insertValues, updateSets, debugParameters);
                }
            }
        }

        private static object? ResolvePropertyPath(object obj, string propertyPath)
        {
            var parts = propertyPath.Split('.');
            object? current = obj;
            foreach (var part in parts)
            {
                var prop = current.GetType().GetProperty(part, BindingFlags.Public | BindingFlags.Instance);
                current = prop?.GetValue(current) ?? current.GetType().GetField(part, BindingFlags.Public | BindingFlags.Instance)?.GetValue(current);

                if (current == null) break;
            }
            return current;
        }

        private static void PrepareForDatabase(string columnName, object? value, MySqlCommand command, List<string> insertColumns, List<string> insertValues, List<string> updateSets, StringBuilder debugParameters)
        {
            if (value is bool boolVal)
                value = boolVal ? 1 : 0;
            else if (value?.GetType().IsEnum == true)
                value = value.ToString();

            var paramName = $"@{columnName}";
            if (!command.Parameters.Contains(paramName))
            {
                command.Parameters.AddWithValue(paramName, value ?? DBNull.Value);
                insertColumns.Add(columnName);
                insertValues.Add(paramName);
                if (!columnName.Equals("id", StringComparison.OrdinalIgnoreCase))
                {
                    updateSets.Add($"{columnName} = VALUES({columnName})");
                }
                debugParameters.AppendLine($"Column: {columnName}, Value: {value}");
            }
        }

        public static async Task<T?> LoadObjectAsync<T>(string tableName, string whereClause, Action<T>? postLoadAction = null) where T : new()
        {
            using (var connection = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await connection.OpenAsync();
                var command = new MySqlCommand($"SELECT * FROM {tableName} WHERE {whereClause};", connection);
                using (var reader = await command.ExecuteReaderAsync())
                {
                    if (!reader.HasRows)
                        return default(T?);

                    T loadedObject = LoadObject<T>(reader);

                    // Check if the action is provided and the loaded object is not null before invoking the action.
                    postLoadAction?.Invoke(loadedObject);

                    return loadedObject;
                }
            }
        }

        //Example Usage for Complex WHERE Clause
        //--------------------------------------
        //This approach allows for complex WHERE clauses, including use of OR, AND, conditional grouping, etc.
        //Here's how you might use this method with a complex WHERE clause:

        //var whereClause = "(Status = @status AND Priority = @priority) OR (DateCreated > @startDate AND DateCreated < @endDate)";
        //var parameters = new Dictionary<string, object>
        //{
        //    {"@status", "Active"},
        //    {"@priority", 1},
        //    {"@startDate", new DateTime(2023, 1, 1)},
        //    {"@endDate", new DateTime(2023, 12, 31)}
        //};

        //var obj = await LoadObjectAsync<MyObject>("MyTable", whereClause, parameters, (o, r) =>
        //{
        //    // Additional operations on loadedObject
        //});

        public static async Task<T?> LoadObjectAsync<T>(
           string tableName,
           string whereClause,
           IDictionary<string, object?>? parameters = null,
           Func<T, MySqlDataReader, Task>? postLoadAction = null)
           where T : new()
        {
            using (var connection = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await connection.OpenAsync();
                var commandText = $"SELECT * FROM {tableName}" + (string.IsNullOrEmpty(whereClause) ? "" : $" WHERE {whereClause}");
                var command = new MySqlCommand(commandText, connection);

                // Add parameters to the command if provided
                if (parameters != null)
                {
                    foreach (var param in parameters)
                    {
                        command.Parameters.AddWithValue(param.Key, param.Value);
                    }
                }

                using (var reader = await command.ExecuteReaderAsync())
                {
                    if (!reader.HasRows)
                        return default(T?);

                    if (!await reader.ReadAsync())
                        return default(T?);

                    T loadedObject = LoadObject<T>(reader);

                    // If an action is provided, invoke it with both the loaded object and the reader
                    if (postLoadAction != null)
                    {
                        await postLoadAction(loadedObject, reader);
                    }

                    return loadedObject;
                }
            }
        }

        public static T LoadObject<T>(MySqlDataReader reader) where T : new()
        {
            T obj = new T();
            var columnToPropertyMap = GenerateColumnPropertyMap(typeof(T));

            foreach (var column in columnToPropertyMap)
            {
                if (!column.Value.excludeFromLoading && reader.GetOrdinal(column.Key) is int ordinal && !reader.IsDBNull(ordinal))
                {
                    var path = column.Value.path.Split('.');
                    var value = reader.GetValue(ordinal);

                    // Navigate to the correct target object based on the path
                    var targetObj = NavigatePath(obj, path[..^1]);

                    if (targetObj is null)
                        continue;

                    // Determine the correct MemberInfo (PropertyInfo or FieldInfo)
                    var memberInfo = (MemberInfo?)targetObj.GetType().GetProperty(path[^1], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) ??
                                        targetObj.GetType().GetField(path[^1], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                    if (memberInfo is null)
                        continue;

                    // Assign the value considering the correct type conversion
                    AssignValueToMember(targetObj, memberInfo, value);
                }
            }

            return obj;
        }

        private static object? NavigatePath(object obj, string[] path)
        {
            object? currentObj = obj;

            foreach (var part in path)
            {
                if (currentObj == null) break; // Safeguard against null references in the chain

                // Try to get the PropertyInfo or FieldInfo for the current part
                var propertyInfo = currentObj.GetType().GetProperty(part, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var fieldInfo = currentObj.GetType().GetField(part, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (propertyInfo != null)
                {
                    // If the property is found, use it
                    var nextObj = propertyInfo.GetValue(currentObj);
                    if (nextObj == null)
                    {
                        // If the property value is null, instantiate a new object of its type and assign it
                        nextObj = Activator.CreateInstance(propertyInfo.PropertyType);
                        propertyInfo.SetValue(currentObj, nextObj);
                    }
                    currentObj = nextObj;
                }
                else if (fieldInfo != null)
                {
                    // Similar handling for fields
                    var nextObj = fieldInfo.GetValue(currentObj);
                    if (nextObj == null)
                    {
                        nextObj = Activator.CreateInstance(fieldInfo.FieldType);
                        fieldInfo.SetValue(currentObj, nextObj);
                    }
                    currentObj = nextObj;
                }
                else
                {
                    // If neither property nor field was found, log an error or throw an exception
                    throw new InvalidOperationException($"Member {part} not found on type {currentObj.GetType().FullName}.");
                }
            }
            return currentObj;
        }

        private static void AssignValueToMember(object targetObj, MemberInfo memberInfo, object value)
        {
            Type targetType = memberInfo switch
            {
                PropertyInfo propertyInfo => Nullable.GetUnderlyingType(propertyInfo.PropertyType) ?? propertyInfo.PropertyType,
                FieldInfo fieldInfo => Nullable.GetUnderlyingType(fieldInfo.FieldType) ?? fieldInfo.FieldType,
                _ => throw new InvalidOperationException("Unsupported member type")
            };

            if (targetType == typeof(bool) && value is int intValue)
            {
                value = intValue != 0;
            }
            else if (targetType.IsEnum && value is string stringValue)
            {
                value = Enum.Parse(targetType, stringValue);
            }
            else
            {
                value = Convert.ChangeType(value, targetType);
            }

            switch (memberInfo)
            {
                case PropertyInfo propertyInfo when propertyInfo.CanWrite:
                    propertyInfo.SetValue(targetObj, value);
                    break;
                case FieldInfo fieldInfo:
                    fieldInfo.SetValue(targetObj, value);
                    break;
            }
        }

        private static Dictionary<string, (MemberInfo member, string path, bool excludeFromLoading)> GenerateColumnPropertyMap(Type type, string parentPath = "")
        {
            var map = new Dictionary<string, (MemberInfo member, string path, bool excludeFromLoading)>();
            var members = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                              .Cast<MemberInfo>()
                              .Concat(type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance));

            foreach (var member in members)
            {
                var dbColumnAttr = member.GetCustomAttribute<DbColumnAttribute>();
                if (dbColumnAttr != null)
                {
                    string fullPath = string.IsNullOrEmpty(parentPath) ? member.Name : $"{parentPath}.{member.Name}";
                    map.Add(dbColumnAttr.Name, (member, fullPath, false));
                }

                var dbIncludeAttr = member.GetCustomAttribute<DbIncludeAttribute>();
                if (dbIncludeAttr != null)
                {
                    Type nestedType = member is PropertyInfo prop ? prop.PropertyType : ((FieldInfo)member).FieldType;
                    string nestedPath = string.IsNullOrEmpty(parentPath) ? member.Name : $"{parentPath}.{member.Name}";
                    var nestedMap = GenerateColumnPropertyMap(nestedType, nestedPath);

                    foreach (var entry in nestedMap)
                    {
                        map.Add(entry.Key, entry.Value);
                    }
                }
            }

            return map;
        }

        public static async Task<List<T>> LoadMultipleAsync<T>(
            string tableName,
            string? whereClause = null,
            IDictionary<string, object?>? parameters = null,
            Func<T, MySqlDataReader, Task>? postLoadAction = null)
            where T : new()
        {
            var results = new List<T>();

            using (var connection = new MySqlConnection(FoxMain.sqlConnectionString))
            {
                await connection.OpenAsync();

                var commandText = $"SELECT * FROM {tableName}" +
                                  (string.IsNullOrEmpty(whereClause) ? "" : $" WHERE {whereClause}");

                using (var command = new MySqlCommand(commandText, connection))
                {
                    if (parameters != null)
                    {
                        foreach (var param in parameters)
                        {
                            command.Parameters.AddWithValue(param.Key, param.Value ?? DBNull.Value);
                        }
                    }

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            T obj = LoadObject<T>(reader);

                            if (postLoadAction != null)
                            {
                                await postLoadAction(obj, reader);
                            }

                            results.Add(obj);
                        }
                    }
                }
            }

            return results;
        }



    }
}
