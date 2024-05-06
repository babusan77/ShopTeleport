/* 更新履歴
 *  1.0.0
 *   初期版
 *
 *  1.0.1
 *   言語ファイルの修正
 *
 *  1.1.0
 *   フレームワークの更新
 *
 *  1.2.0
 *   キャンセルコマンドの追加
 */

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Configuration;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("ShopTeleport", "babu77", "1.2.0")]
    [Description("simple teleport plugin")]
    
    public class ShopTeleport : RustPlugin
    {
        #region Declarations
        private const string PName = "<color=yellow>[ShopTeleport]:</color>";
        const string PermUse = "shopteleport.use";
        const string PermSet = "shopteleport.set";
        private DynamicConfigFile _shopPosData;
        private Dictionary<string, Vector3> _posData;
        private Dictionary<string, Timer> _userTimer = new Dictionary<string, Timer>();
        #endregion
        
        #region Configurations
        #endregion
        
        #region OxideHooks
        private void Init()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                {"NoPerm", "You don't have a permission."},
                {"UpdatePos", "The current position is saved."},
                {"NotSet", "The teleport destination is not set."},
                {"BeforeTeleport", "You will teleport to shop in 15 seconds."},
                {"AlreadyStartTimer", "The teleport command is already being executed at present."},
                {"NotStartTimer", "There are no teleportation instructions that can be cancelled."},
                {"CancelTimer", "Teleport command cancelled."},
                {"CancelTimerFailure", "Cancellation failed."}
            }, this);
        }
        
        private void Loaded()
        {
            permission.RegisterPermission(PermUse, this);
            permission.RegisterPermission(PermSet, this);
            
            _shopPosData = GetFile(nameof(ShopTeleport));
            _posData = _shopPosData.ReadObject<Dictionary<string, Vector3>>();
        }
        
        private void OnServerSave()
        {
            SavePosData();
        }
        
        private void OnServerShutdown() => OnServerSave();
        
        private void Unload() => OnServerSave();
        
        #endregion

        #region Classes
        
        #endregion
        
        
        #region Functions
        private DynamicConfigFile GetFile(string name)
        {
            var file = Interface.Oxide.DataFileSystem.GetFile(name);
            file.Settings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
            file.Settings.Converters = new JsonConverter[]
            {
                new UnityVector3Converter(),
                new CustomComparerDictionaryCreationConverter<string>(StringComparer.OrdinalIgnoreCase)
            };
            return file;
        }
        private void SavePosData()
        {
            _shopPosData.WriteObject(_posData);
        }
        
        public void Teleport(BasePlayer player, Vector3 position)
        {
            if (player.net?.connection != null)
                player.ClientRPCPlayer(null, player, "StartLoading");
            
            StartSleeping(player);
            player.SetParent(null, true, true);
            player.MovePosition(position);
            UpgradeNw(player, position);
        }
        
        private void StartSleeping(BasePlayer player)
        {
            if (player.IsSleeping())
                return;
            player.SetPlayerFlag(BasePlayer.PlayerFlags.Sleeping, true);
            if (!BasePlayer.sleepingPlayerList.Contains(player))
                BasePlayer.sleepingPlayerList.Add(player);
            player.CancelInvoke("InventoryUpdate");
        }
        
        private void UpgradeNw(BasePlayer player,Vector3 position)
        {
            if (player.net?.connection != null)
                player.ClientRPCPlayer(null, player, "ForcePositionTo", position);
            if (player.net?.connection != null)
                player.SetPlayerFlag(BasePlayer.PlayerFlags.ReceivingSnapshot, true);
            player.UpdateNetworkGroup();
            player.SendNetworkUpdateImmediate(false);
            if (player.net?.connection == null) return;
            try
            {
                player.ClearEntityQueue(null);
            }
            catch
            {
            }
            player.SendFullSnapshot();
        }
        
        private void SetPos(BasePlayer player)
        {
            Vector3 playerPos = player.transform.position;
            _posData["pos"] = playerPos;
            SendMessage(player, lang.GetMessage("UpdatePos", this));
        }

        private void TeleportShop(BasePlayer player)
        {
            Vector3 pos;
            if (_posData.TryGetValue("pos", out pos))
            {
                if (_userTimer.ContainsKey(player.UserIDString))
                {
                    SendMessage(player, lang.GetMessage("AlreadyStartTimer", this));
                    return;
                }
                
                _userTimer.Add(player.UserIDString, 
                    timer.Once(15f, () =>
                {
                    Teleport(player, pos);
                    Timer userTimer;
                    if (!_userTimer.TryGetValue(player.UserIDString, out userTimer)) return;
                    userTimer.Destroy();
                    _userTimer.Remove(player.UserIDString);
                }));
            }
            else
            {
                SendMessage(player, lang.GetMessage("NotSet", this));
            }
        }

        private void TeleportCancel(BasePlayer player)
        {
            Timer userTimer;
            if (!_userTimer.TryGetValue(player.UserIDString, out userTimer))
            {
                SendMessage(player, lang.GetMessage("NotStartTimer", this));
                return;
            }
            else
            {
                userTimer.Destroy();
                if (userTimer.Destroyed)
                {
                    _userTimer.Remove(player.UserIDString);
                    SendMessage(player, lang.GetMessage("CancelTimer", this));
                }
                else
                {
                    SendMessage(player, lang.GetMessage("CancelTimerFailure", this));
                }
            }
        }
        #endregion
        
        #region Commands
        [ChatCommand("shop")]
        private void ShopCommand(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, PermUse))
            {
                SendMessage(player, lang.GetMessage("NoPerm", this));
                return;
            }
            
            if (0 < args.Length && args[0].ToLower().Equals("set"))
            {
                if (!permission.UserHasPermission(player.UserIDString, PermSet))
                {
                    SendMessage(player, lang.GetMessage("NoPerm", this));
                }
                else
                {
                    SetPos(player);
                }
            }
            else if (0 < args.Length && (args[0].ToLower().Equals("cancel")||args[0].ToLower().Equals("c")))
            {
                if (!permission.UserHasPermission(player.UserIDString, PermUse))
                {
                    SendMessage(player, lang.GetMessage("NoPerm", this));
                }
                else
                {
                    TeleportCancel(player);
                }
            }
            else
            {
                SendMessage(player, lang.GetMessage("BeforeTeleport", this));
                TeleportShop(player);
            }
        }
        #endregion
        
        #region Helper
        private class UnityVector3Converter : JsonConverter
        {
            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                var vector = (Vector3)value;
                writer.WriteValue($"{vector.x} {vector.y} {vector.z}");
            }
            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            {
                if (reader.TokenType == JsonToken.String)
                {
                    var values = reader.Value.ToString().Trim().Split(' ');
                    return new Vector3(Convert.ToSingle(values[0]), Convert.ToSingle(values[1]), Convert.ToSingle(values[2]));
                }
                var o = JObject.Load(reader);
                return new Vector3(Convert.ToSingle(o["x"]), Convert.ToSingle(o["y"]), Convert.ToSingle(o["z"]));
            }
            public override bool CanConvert(Type objectType)
            {
                return objectType == typeof(Vector3);
            }
        }
        private class CustomComparerDictionaryCreationConverter<T> : CustomCreationConverter<IDictionary>
        {
            private readonly IEqualityComparer<T> comparer;
            public CustomComparerDictionaryCreationConverter(IEqualityComparer<T> comparer)
            {
                if (comparer == null) throw new ArgumentNullException(nameof(comparer));
                this.comparer = comparer;
            }
            public override bool CanConvert(Type objectType)
            {
                return HasCompatibleInterface(objectType) && HasCompatibleConstructor(objectType);
            }
            private static bool HasCompatibleInterface(Type objectType)
            {
                return objectType.GetInterfaces().Where(i => HasGenericTypeDefinition(i, typeof(IDictionary<,>))).Any(i => typeof(T).IsAssignableFrom(i.GetGenericArguments().First()));
            }
            private static bool HasGenericTypeDefinition(Type objectType, Type typeDefinition)
            {
                return objectType.IsGenericType && objectType.GetGenericTypeDefinition() == typeDefinition;
            }
            private static bool HasCompatibleConstructor(Type objectType)
            {
                return objectType.GetConstructor(new[] { typeof(IEqualityComparer<T>) }) != null;
            }
            public override IDictionary Create(Type objectType)
            {
                return Activator.CreateInstance(objectType, comparer) as IDictionary;
            }
        }
        void SendMessage(BasePlayer player, string message, params object[] args) => PrintToChat(player, PName + message, args);
        private void Log(string text) => LogToFile("death", $"[{DateTime.Now}] {text}", this);
        #endregion
    }
}