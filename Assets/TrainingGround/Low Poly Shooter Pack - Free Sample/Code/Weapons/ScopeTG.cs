// Copyright 2021, Infima Games. All Rights Reserved.

using System;
using UnityEngine;

namespace InfimaGames.LowPolyShooterPack
{
    /// <summary>
    /// Weapon Scope.
    /// </summary>
    public class ScopeTG : ScopeBehaviourTG
    {
        #region FIELDS SERIALIZED

        [Header("Interface")]

        [Tooltip("Interface Sprite.")]
        [SerializeField]
        private Sprite sprite;
        
        #endregion

        #region GETTERS
        
        public override Sprite GetSprite() => sprite;

        #endregion
    }
}