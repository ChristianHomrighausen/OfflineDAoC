using System;

namespace DOL.GS.Commands
{
    /// <summary>
    /// /botlevelcap on the selected autonomous gamebot: show, set (1-50) or remove its personal level cap.
    /// Caps are saved at once to bot-levelcaps.json (BotLevelCapStore).
    /// </summary>
    [Cmd("&botlevelcap",
        ePrivLevel.Player,
        "Show or set the selected gamebot's personal level cap",
        "/botlevelcap - show the selected bot's level cap",
        "/botlevelcap <1-50> - set a level cap",
        "/botlevelcap off - remove the level cap")]
    public class BotLevelCapCommandHandler : AbstractCommandHandler, ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args)
        {
            if (client?.Player == null || IsSpammingCommand(client.Player, "botlevelcap"))
                return;

            if (client.Player.TargetObject is not GameBot { IsAutonomousWorldBot: true } bot || bot.DatabaseID <= 0)
            {
                DisplayMessage(client, "Bitte zuerst einen Gamebot markieren.");
                DisplaySyntax(client);
                return;
            }

            if (BotLevelCapStore.ExperiencePaused)
            {
                DisplayMessage(client, "Levelcap-Datei konnte nicht geladen werden. Datei korrigieren und Server neu starten.");
                return;
            }

            string botLabel = $"{bot.Name}, Bot-ID {bot.DatabaseID}";
            try
            {
                if (args.Length < 2)
                {
                    DisplayMessage(client, BotLevelCapStore.TryGetCap(bot.DatabaseID, out byte current)
                        ? $"Persoenliches Levelcap fuer {botLabel}: Levelcap {current}."
                        : $"{botLabel}: kein persoenliches Levelcap.");
                    return;
                }

                if (string.Equals(args[1], "off", StringComparison.OrdinalIgnoreCase))
                {
                    BotLevelCapStore.SetCap(bot.DatabaseID, null);
                    DisplayMessage(client, $"Persoenliches Levelcap fuer {botLabel} entfernt und gespeichert.");
                    return;
                }

                if (!int.TryParse(args[1], out int cap) || cap < BotLevelCapStore.MinCap || cap > BotLevelCapStore.MaxCap)
                {
                    DisplayMessage(client, "Bitte eine ganze Zahl von 1 bis 50 oder off angeben.");
                    return;
                }

                if (bot.Level > cap)
                {
                    DisplayMessage(client, $"{bot.Name} ist bereits Level {bot.Level}. Das Cap darf nicht darunter liegen.");
                    return;
                }

                BotLevelCapStore.SetCap(bot.DatabaseID, (byte)cap);
                DisplayMessage(client, $"Levelcap {cap} fuer {botLabel} gespeichert. Ab Level {cap} gibt es keine neue XP.");
            }
            catch (Exception e)
            {
                DisplayMessage(client, "Levelcap konnte nicht gelesen oder gespeichert werden: " + e.Message);
            }
        }
    }
}
