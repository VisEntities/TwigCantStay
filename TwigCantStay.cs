/*
 * Copyright (C) 2024 Game4Freak.io
 * This mod is provided under the Game4Freak EULA.
 * Full legal terms can be found at https://game4freak.io/eula/
 */

using Newtonsoft.Json;
using Oxide.Core;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using static BuildingManager;

namespace Oxide.Plugins
{
    [Info("Twig Cant Stay", "VisEntities", "1.1.0")]
    [Description("Limits the number of twig blocks a building can have.")]
    public class TwigCantStay : RustPlugin
    {
        #region Fields

        private static TwigCantStay _plugin;
        private static Configuration _config;
        private StoredData _storedData;

        #endregion Fields

        #region Configuration

        private class Configuration
        {
            [JsonProperty("Version")]
            public string Version { get; set; }

            [JsonProperty("Maximum Number Of Twig Blocks Allowed Per Building")]
            public int MaximumNumberOfTwigBlocksAllowedPerBuilding { get; set; }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            _config = Config.ReadObject<Configuration>();

            if (string.Compare(_config.Version, Version.ToString()) < 0)
                UpdateConfig();

            SaveConfig();
        }

        protected override void LoadDefaultConfig()
        {
            _config = GetDefaultConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(_config, true);
        }

        private void UpdateConfig()
        {
            PrintWarning("Config changes detected! Updating...");

            Configuration defaultConfig = GetDefaultConfig();

            if (string.Compare(_config.Version, "1.0.0") < 0)
                _config = defaultConfig;

            PrintWarning("Config update complete! Updated from version " + _config.Version + " to " + Version.ToString());
            _config.Version = Version.ToString();
        }

        private Configuration GetDefaultConfig()
        {
            return new Configuration
            {
                Version = Version.ToString(),
                MaximumNumberOfTwigBlocksAllowedPerBuilding = 16
            };
        }

        #endregion Configuration

        #region Data Utility

        public class DataFileUtil
        {
            private const string MAIN_FOLDER = "";

            public static string GetFilePath(string fileName = null)
            {
                if (fileName == null)
                    fileName = _plugin.Name;

                return Path.Combine(MAIN_FOLDER, fileName);
            }

            public static string[] GetAllFilePaths()
            {
                string[] filePaths = Interface.Oxide.DataFileSystem.GetFiles(MAIN_FOLDER);

                for (int i = 0; i < filePaths.Length; i++)
                {
                    // Remove the redundant '.json' from the filepath. This is necessary because the filepaths are returned with a double '.json'.
                    filePaths[i] = filePaths[i].Substring(0, filePaths[i].Length - 5);
                }

                return filePaths;
            }

            public static bool Exists(string filePath)
            {
                return Interface.Oxide.DataFileSystem.ExistsDatafile(filePath);
            }

            public static T Load<T>(string filePath) where T : class, new()
            {
                T data = Interface.Oxide.DataFileSystem.ReadObject<T>(filePath);
                if (data == null)
                    data = new T();

                return data;
            }

            public static T LoadIfExists<T>(string filePath) where T : class, new()
            {
                if (Exists(filePath))
                    return Load<T>(filePath);
                else
                    return null;
            }

            public static T LoadOrCreate<T>(string filePath) where T : class, new()
            {
                T data = LoadIfExists<T>(filePath);
                if (data == null)
                    data = new T();

                return data;
            }

            public static void Save<T>(string filePath, T data)
            {
                Interface.Oxide.DataFileSystem.WriteObject<T>(filePath, data);
            }

            public static void Delete(string filePath)
            {
                Interface.Oxide.DataFileSystem.DeleteDataFile(filePath);
            }
        }

        #endregion Data Utility

        #region Stored Data

        public class StoredData
        {
            [JsonProperty("Building Twigs")]
            public Dictionary<uint, HashSet<ulong>> BuildingTwigs { get; set; } = new Dictionary<uint, HashSet<ulong>>();
        }

        #endregion Stored Data

        #region Oxide Hooks

        private void Init()
        {
            _plugin = this;
            PermissionUtil.RegisterPermissions();
            _storedData = DataFileUtil.LoadOrCreate<StoredData>(DataFileUtil.GetFilePath());
        }

        private void Unload()
        {
            _config = null;
            _plugin = null;
        }

        private void OnStructureUpgrade(BuildingBlock buildingBlock, BasePlayer player, BuildingGrade.Enum grade)
        {
            if (buildingBlock == null || player == null || grade == BuildingGrade.Enum.Twigs)
                return;

            uint buildingId = buildingBlock.buildingID;
            ulong buildingBlockId = buildingBlock.net.ID.Value;

            if (_storedData.BuildingTwigs.ContainsKey(buildingId) && _storedData.BuildingTwigs[buildingId].Contains(buildingBlockId))
            {
                _storedData.BuildingTwigs[buildingId].Remove(buildingBlockId);

                if (_storedData.BuildingTwigs[buildingId].Count == 0)
                    _storedData.BuildingTwigs.Remove(buildingId);

                DataFileUtil.Save(_plugin.Name, _storedData);
            }
        }

        private void OnEntityBuilt(Planner planner, GameObject gameObject)
        {
            if (planner == null || gameObject == null)
                return;

            BasePlayer player = planner.GetOwnerPlayer();
            if (player == null)
                return;

            BuildingBlock buildingBlock = gameObject.ToBaseEntity() as BuildingBlock;
            if (buildingBlock == null || buildingBlock.grade != BuildingGrade.Enum.Twigs)
                return;

            uint buildingId = buildingBlock.buildingID;
            ulong buildingBlockId = buildingBlock.net.ID.Value;

            if (!_storedData.BuildingTwigs.ContainsKey(buildingId))
            {
                _storedData.BuildingTwigs[buildingId] = new HashSet<ulong>();
            }

            _storedData.BuildingTwigs[buildingId].Add(buildingBlockId);
            DataFileUtil.Save(_plugin.Name, _storedData);
        }

        private object CanBuild(Planner planner, Construction prefab, Construction.Target target)
        {
            if (planner == null || prefab == null || target.entity == null)
                return null;

            BasePlayer player = planner.GetOwnerPlayer();
            if (player == null || PermissionUtil.HasPermission(player, PermissionUtil.IGNORE))
                return null;

            if (!prefab.fullName.Contains("building core"))
                return null;

            BuildingBlock targetBuildingBlock = target.entity as BuildingBlock;
            if (targetBuildingBlock == null)
                return null;

            uint buildingId = targetBuildingBlock.buildingID;

            bool isOwnerOrTeammate = targetBuildingBlock.OwnerID == player.userID || AreTeammates(targetBuildingBlock.OwnerID, player.userID);
            bool isAuthorized = false;
            Building building = TryGetBuildingForEntity(targetBuildingBlock, minimumBuildingBlocks: 1, mustHaveBuildingPrivilege: true);
            if (building != null)
            {
                isAuthorized = building.buildingPrivileges.Any(priv => priv.IsAuthed(player));
            }

            if (isOwnerOrTeammate || isAuthorized)
            {
                if (_storedData.BuildingTwigs.ContainsKey(buildingId) && _storedData.BuildingTwigs[buildingId].Count >= _config.MaximumNumberOfTwigBlocksAllowedPerBuilding)
                {
                    SendMessage(player, Lang.CannotBuildTwig, _config.MaximumNumberOfTwigBlocksAllowedPerBuilding);
                    return true;
                }
            }

            return null;
        }

        private void OnEntityKill(BuildingBlock buildingBlock)
        {
            if (buildingBlock == null || buildingBlock.grade != BuildingGrade.Enum.Twigs)
                return;

            uint buildingId = buildingBlock.buildingID;
            ulong buildingBlockId = buildingBlock.net.ID.Value;

            if (_storedData.BuildingTwigs.ContainsKey(buildingId) && _storedData.BuildingTwigs[buildingId].Contains(buildingBlockId))
            {
                _storedData.BuildingTwigs[buildingId].Remove(buildingBlockId);

                if (_storedData.BuildingTwigs[buildingId].Count == 0)
                    _storedData.BuildingTwigs.Remove(buildingId);

                DataFileUtil.Save(_plugin.Name, _storedData);
            }
        }

        #endregion Oxide Hooks

        #region Helper Functions

        public static Building TryGetBuildingForEntity(BaseEntity entity, int minimumBuildingBlocks, bool mustHaveBuildingPrivilege = true)
        {
            BuildingBlock buildingBlock = entity as BuildingBlock;
            DecayEntity decayEntity = entity as DecayEntity;

            uint buildingId = 0;
            if (buildingBlock != null)
            {
                buildingId = buildingBlock.buildingID;
            }
            else if (decayEntity != null)
            {
                buildingId = decayEntity.buildingID;
            }

            Building building = server.GetBuilding(buildingId);
            if (building != null &&
                building.buildingBlocks.Count >= minimumBuildingBlocks &&
                (!mustHaveBuildingPrivilege || building.HasBuildingPrivileges()))
            {
                return building;
            }

            return null;
        }

        public static bool AreTeammates(ulong firstPlayerId, ulong secondPlayerId)
        {
            RelationshipManager.PlayerTeam team = RelationshipManager.ServerInstance.FindPlayersTeam(firstPlayerId);
            if (team != null && team.members.Contains(secondPlayerId))
                return true;

            return false;
        }

        #endregion Helper Functions

        #region Permissions

        private static class PermissionUtil
        {
            public const string IGNORE = "twigcantstay.ignore";
            private static readonly List<string> _permissions = new List<string>
            {
                IGNORE,
            };

            public static void RegisterPermissions()
            {
                foreach (var permission in _permissions)
                {
                    _plugin.permission.RegisterPermission(permission, _plugin);
                }
            }

            public static bool HasPermission(BasePlayer player, string permissionName)
            {
                return _plugin.permission.UserHasPermission(player.UserIDString, permissionName);
            }
        }

        #endregion Permissions

        #region Localization

        private class Lang
        {
            public const string CannotBuildTwig = "CannotBuildTwig";
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                [Lang.CannotBuildTwig] = "You cannot build more twig blocks in this building. The limit of {0} has been reached. Upgrade or remove existing twig blocks to build more."
            }, this, "en");
        }

        private void SendMessage(BasePlayer player, string messageKey, params object[] args)
        {
            string message = lang.GetMessage(messageKey, this, player.UserIDString);
            if (args.Length > 0)
                message = string.Format(message, args);

            SendReply(player, message);
        }

        #endregion Localization
    }
}