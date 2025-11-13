// Copyright 2021, Infima Games. All Rights Reserved.

namespace InfimaGames.LowPolyShooterPack
{
    /// <summary>
    /// Game Mode Service.
    /// </summary>
    public interface IGameModeServiceTG : IGameServiceTG
    {
        /// <summary>
        /// Returns the Player Character.
        /// </summary>
        CharacterBehaviourTG GetPlayerCharacter();
    }
}