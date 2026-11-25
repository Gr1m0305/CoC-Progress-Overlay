namespace ClashOverlay.Core.Data;

/// <summary>
/// A provider of static game data. The app ships with <see cref="ClashKingDataSource"/>; another data set can be
/// swapped in by implementing this interface.
/// </summary>
public interface IGameDataSource
{
    /// <summary>Short name for About/Settings, e.g. "ClashKing".</summary>
    string Name { get; }

    /// <summary>True when the data files are present locally and <see cref="Load"/> can run.</summary>
    bool IsAvailable { get; }

    /// <summary>Reads and normalises the data. Throws <see cref="GameDataException"/> when it is unusable.</summary>
    GameData Load();
}

public sealed class GameDataException(string message, Exception? inner = null) : Exception(message, inner);
