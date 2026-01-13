using RolmarAllegroProductsSyncService.Models;
using RolmarAllegroProductsSyncService.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace RolmarAllegroProductsSyncService.Services.Rolmar
{
    public class FileSyncStateService : ISyncStateService
    {
        private readonly string _filePath = Path.Combine(
            AppContext.BaseDirectory,
            "sync-state.json");

        public async Task<string?> GetLastCategoriesNameAsync()
        {
            if (!File.Exists(_filePath))
                return null;

            var json = await File.ReadAllTextAsync(_filePath);
            var state = JsonSerializer.Deserialize<SyncState>(json);
            return state?.LastCategoriesName;
        }

        public async Task SetLastCategoriesNameAsync(string categoriesName)
        {
            var state = new SyncState
            {
                LastCategoriesName = categoriesName
            };

            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            await File.WriteAllTextAsync(_filePath, json);
        }
    }
}