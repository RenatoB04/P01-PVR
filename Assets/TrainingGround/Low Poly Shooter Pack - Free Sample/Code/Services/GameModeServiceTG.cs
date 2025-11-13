// Copyright 2021, Infima Games. All Rights Reserved.

namespace InfimaGames.LowPolyShooterPack
{
    /// <summary>
    /// Game Mode Service.
    /// </summary>
    public class GameModeServiceTG : IGameModeServiceTG
    {
        #region FIELDS
        
        /// <summary>
        /// The Player Character.
        /// </summary>
        private CharacterBehaviourTG playerCharacter;
        
        #endregion
        
        #region FUNCTIONS
        
        public CharacterBehaviourTG GetPlayerCharacter()
        {
            //Make sure we have a player character that is good to go!
            if (playerCharacter == null)
                playerCharacter = UnityEngine.Object.FindObjectOfType<CharacterBehaviourTG>();
            
            //Return.
            return playerCharacter;
        }
        
        #endregion
    }
}