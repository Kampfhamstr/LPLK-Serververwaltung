namespace LPLK_Sounds;

public sealed record SoundRecipient(int Slot, ulong SteamId);

public static class SoundSendPolicy
{
    // Directed sounds never have an override: the receiver always controls hearing.
    public static string? Rejection(bool sourceAlive, bool deadOnlySetting, SoundListener target,
        SoundListeningPreference preference, string soundId)
    {
        if (target.Slot is < 0 or >= 64 || target.SteamId == 0 || !target.Valid || !target.Authorized || target.Fake)
            return "Dieser Spieler ist nicht mehr verfuegbar.";
        if (!preference.Enabled) return "Dieser Spieler hat die Sounds ausgeschaltet.";
        if (preference.VolumePercent == 0) return "Dieser Spieler hat seine Soundlautstaerke auf 0% gestellt.";
        if (preference.MutedSounds.Contains(soundId)) return "Dieser Spieler hat diesen Sound stummgeschaltet.";
        if (SoundAudience.RestrictToDead(sourceAlive, deadOnlySetting) && target.Alive)
            return "Tote und Zuschauer duerfen Sounds nur an Tote und Zuschauer senden.";
        if (!(target.Alive ? preference.HearAlive : preference.HearDead))
            return "Dieser Spieler hat Sounds fuer seinen aktuellen Zustand ausgeschaltet.";
        return null;
    }
}
