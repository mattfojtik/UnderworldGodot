using System.Diagnostics;
using Godot;

namespace Underworld
{
    /// <summary>
    /// Class for interactions involving missile combat and projectile spell hits
    /// </summary>
    public partial class combat : UWClass
    {
        /// <summary>Self-hit / missile diagnosis — console + vr_diag.log.</summary>
        public static void LogMissileDiag(string message) => VrDiagLog.Print($"[MISSILE-DIAG] {message}");

        public static void MissileImpact(uwObject projectile, uwObject objectHit)
        {
            var diDamageMultipler = 1;
            var hitIsPlayer = objectHit == playerdat.playerObject || objectHit?.index == 1;
            var sourceIsPlayer = projectile.ProjectileSourceID == 1
                || projectile.ProjectileSourceID == playerdat.playerObject?.index;
            var sourceIsHit = projectile.ProjectileSourceID != 0
                && projectile.ProjectileSourceID == objectHit.index;

            var p = projectile.GetCoordinate();
            var h = objectHit.GetCoordinate();
            var dx = p.X - h.X;
            var dy = p.Y - h.Y;
            var dz = p.Z - h.Z;
            var horiz = Mathf.Sqrt(dx * dx + dz * dz);
            // Prefer XR camera XZ in native VR so room-scale head ≠ UW body marker can't fool the gate.
            var casterWorld = h;
            var casterLabel = "hit.GetCoordinate";
            if (hitIsPlayer && VrController.IsActive && !uwsettings.instance.vr_mirror)
            {
                var cam = VrController.GetDiagCameraWorldPosition();
                if (cam.HasValue)
                {
                    casterWorld = cam.Value;
                    casterLabel = "xrCamera";
                    dx = p.X - casterWorld.X;
                    dz = p.Z - casterWorld.Z;
                    horiz = Mathf.Sqrt(dx * dx + dz * dz);
                }
            }

            if (hitIsPlayer || sourceIsPlayer || sourceIsHit)
            {
                LogMissileDiag(
                    $"MissileImpact ENTER proj={projectile.a_name} id=0x{projectile.item_id:X} idx={projectile.index} "
                    + $"src={projectile.ProjectileSourceID} hit={objectHit.a_name} hitIdx={objectHit.index} "
                    + $"sourceIsHit={sourceIsHit} hitIsPlayer={hitIsPlayer} maj/min={projectile.majorclass}/{projectile.minorclass} "
                    + $"projTile=({projectile.tileX},{projectile.tileY}) hitTile=({objectHit.tileX},{objectHit.tileY}) "
                    + $"projPos=({p.X:F2},{p.Y:F2},{p.Z:F2}) hitPos=({h.X:F2},{h.Y:F2},{h.Z:F2}) "
                    + $"casterRef={casterLabel}=({casterWorld.X:F2},{casterWorld.Y:F2},{casterWorld.Z:F2}) "
                    + $"horiz={horiz:F2}m vert={dy:F2}m "
                    + $"bit15_7={projectile.UnkBit_0X15_Bit7} dseg_25BC={UWMotionParamArray.dseg_67d6_25BC}");
            }
            else
            {
                Debug.Print($"Missile impact {projectile.a_name} on {objectHit.a_name}");
            }

            if (_RES==GAME_UW2 && projectile.item_id == 0x1E)
            {
                //projectile is a UW2 Satellite
                if (projectile.ProjectileSourceID == objectHit.index)
                {
                    //satellite has hit it's caster
                    LogMissileDiag("MissileImpact SKIP satellite→caster");
                    projectile.UnkBit_0X15_Bit7 = 0;
                    return ;
                }
            }

            // DOS allows real run-into self-hits; reject phantom long-range caster hits
            // (VR laser spawn / tile-AABB). Gate lives here so every Use() path is covered.
            if (sourceIsHit)
            {
                const float maxHorizontalMeters = 0.9f;
                if (horiz > maxHorizontalMeters)
                {
                    LogMissileDiag(
                        $"MissileImpact SKIP self-hit (horiz={horiz:F2}m > {maxHorizontalMeters}m, casterRef={casterLabel})");
                    projectile.UnkBit_0X15_Bit7 = 0;
                    return;
                }

                LogMissileDiag(
                    $"MissileImpact ALLOW self-hit (horiz={horiz:F2}m <= {maxHorizontalMeters}m, casterRef={casterLabel})");
            }

            var MissileDamage = rangedObjectDat.damage(projectile.item_id);
            if (projectile.ProjectileSourceID == 1)
            {
                //player has launched the projectile
                if (rangedObjectDat.RangedWeaponType(projectile.item_id) == 0xC0)
                {
                    diDamageMultipler = (playerdat.Missile<<3) + 0xC0;
                    var SkillCheckResult = playerdat.SkillCheck(playerdat.Missile, 0xA);
                    switch (SkillCheckResult)
                    {
                        case playerdat.SkillCheckResult.CritFail:
                            diDamageMultipler -= 0x80;
                            break;
                        case playerdat.SkillCheckResult.CritSucess:
                            diDamageMultipler += 0xC0;
                            break;
                        default:
                            break;
                    }
                    MissileDamage = (MissileDamage * diDamageMultipler) >> 8;
                }
            }

            int var4_x;
            int var6_y;

            if (UWMotionParamArray.dseg_67d6_25BC==0)
            {
                var4_x = UWMotionParamArray.UnknownX_dseg_67d6_25BD;
                var6_y = UWMotionParamArray.UnknownY_dseg_67d6_25BE;
            }
            else
            {
                var4_x = UWMotionParamArray.dseg_67d6_25BF_X;
                var6_y = UWMotionParamArray.dseg_67d6_25C0_Y;
            }

            if (hitIsPlayer)
            {
                LogMissileDiag(
                    $"MissileImpact → MissileAttackHit dmg={MissileDamage} "
                    + $"dmgType={-rangedObjectDat.RangedWeaponType(projectile.item_id)} "
                    + $"hitXY=({var4_x},{var6_y})");
            }

            MissileAttackHit(
                projectileSource: projectile.ProjectileSourceID, 
                Projectile: projectile, 
                objectHit: objectHit, 
                X: var4_x, Y: var6_y, 
                damage: MissileDamage, 
                damageType: (byte)-rangedObjectDat.RangedWeaponType(projectile.item_id));

            if (objectHit.majorclass == 1)
            {
                projectile.UnkBit_0XA_Bit7 = 1;
            }

        }


        /// <summary>
        /// Applies the missile hit
        /// </summary>
        /// <param name="projectileSource"></param>
        /// <param name="Projectile"></param>
        /// <param name="objectHit"></param>
        /// <param name="X"></param>
        /// <param name="Y"></param>
        /// <param name="damage"></param>
        /// <param name="damageType"></param>
        static void MissileAttackHit(int projectileSource, uwObject Projectile, uwObject objectHit, int X, int Y, int damage, byte damageType)
        {
            if (objectHit == playerdat.playerObject || objectHit?.index == 1)
            {
                LogMissileDiag(
                    $"MissileAttackHit player dmg={damage} type={damageType} "
                    + $"src={projectileSource} proj={Projectile?.a_name} idx={Projectile?.index} "
                    + $"tile=({X},{Y})");
            }
            else
            {
                Debug.Print("Missile Attack Hit");
            }

            DefendingCharacter = objectHit;
            CombatHitTileX = X; CombatHitTileY = Y;
            AttackWasACrit = false;
            AttackScoreFlankingBonus = 0;
            BodyPartHit = PickBodyHitPoint(
                defenderZ: objectHit.zpos, 
                defenderTop: commonObjDat.height(objectHit.item_id) + objectHit.zpos, 
                attackerZ: Projectile.zpos, 
                attackerTop: commonObjDat.height(Projectile.item_id));

            AttackDamage = damage;
            if (projectileSource == 1)
            {
                PlayerAttackCharge = 0x80;
            }
            else
            {
                NPCFinalAttackCharge = 0x80;                
            }


            if (objectHit == playerdat.playerObject)
            {
                //sound effect at player position
                UWsoundeffects.PlaySoundEffectAtAvatar(UWsoundeffects.SoundEffectHit1, pan: 0x40, velocityOffset: 0);
            }
            else
            {
                //sound effect at hit position.
                UWsoundeffects.PlaySoundEffectAtObject(UWsoundeffects.SoundEffectHit2, objectHit, 0);
            }
            AttackerAppliesFinalDamage(
                attacker: projectileSource,
                damageType: damageType, 
                MissileAttack: true);
            
        }

    }//end namespace
}//end class
