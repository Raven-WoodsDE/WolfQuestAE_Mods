using UnityEngine;
using UnityEngine.UI;
using WolfQuestEp3;
using SharedCommons;

namespace AltHunt
{
    public class HuntManager : MonoBehaviour
    {
        private static Animal Wolf, Prey;
        private static float trueHealth_wolf, trueHealth_prey;
        private static float wolfDamage, preyDamage;

        private float tmrDamageBite;
        private Vector3 posPlayer, posPrey;

        private Animation anim;
        private GameObject[] backgrounds;
        private Image health_wolf, health_prey;

        private const string start_success = "clip_start_success";
        private const string start_failed = "clip_start_failed";
        private const string start_small = "clip_start_small";
        private const string end_success = "clip_end_success";
        private const string end_failed = "clip_end_failed";
        private const string loop_bite = "clip_loop_bite";

        private const GroundingMode gm =
            GroundingMode.GroundUpwardOrDownward;

        private const float animSpeed = .15f;
        private const float damageMult = 4f;
        private const float maxDistanceForHuntBonus = 15f;

        private float tmrBiteSound;

        AudioSource au;

        public void Init(Animal pPlayer, Animal pPrey)
        {
            Wolf = pPlayer;
            trueHealth_wolf = Wolf.State.Health;
            posPlayer = Wolf.Position;

            Prey = pPrey;
            trueHealth_prey = Prey.State.Health;
            posPrey = Prey.Position;

            wolfDamage = GetWolfDamage();
            preyDamage = GetBaseDamagePrey();

            health_wolf = GameObject.Find("ah_health_wolf").GetComponent<Image>();
            health_wolf.fillAmount = trueHealth_wolf / Wolf.State.MaxHealth;

            health_prey = GameObject.Find("ah_health_prey").GetComponent<Image>();
            health_prey.fillAmount = trueHealth_prey / Prey.State.MaxHealth;

            backgrounds = new GameObject[3];
            backgrounds[0] = GameObject.Find("ah_animation_bg1");
            backgrounds[1] = GameObject.Find("ah_animation_bg2");
            backgrounds[2] = GameObject.Find("ah_animation_bg3");
            for (int i = 0; i < backgrounds.Length; i++)
            {
                backgrounds[i].SetActive(false);
            }

            backgrounds[Random.Range(0, 2)].SetActive(true);

            anim = GameObject.Find("ah_animation").GetComponent<Animation>();
            anim[start_success].speed = animSpeed;
            anim[start_failed].speed = animSpeed;
            anim[start_small].speed = animSpeed;
            anim[end_success].speed = animSpeed;
            anim[end_failed].speed = animSpeed;
            anim[loop_bite].speed = animSpeed;

            InputControls.DisableCameraInput = true;
            InputControls.DisableInput = true;

            GameObject audio = new GameObject("audio");
            audio.transform.parent = transform;
            au = audio.AddComponent<AudioSource>();
            au.transform.localPosition = Vector3.zero;
            au.loop = false;
            au.volume = .5f;
            au.bypassEffects = true;
            au.pitch = Random.Range(.9f, 1.1f);
            au.reverbZoneMix = 0f;

            au.clip = Plugin.sndStart;
            au.Play();

            string speciesName = Prey.Species.name.ToLower();
            switch (speciesName)
            {
                case "beaver":
                case "fox":
                case "coyote":
                case "wolf" when Plugin.IsStrayPup(Prey):
                    anim.Play(start_small);
                    break;

                default:
                if (Plugin.ValidSpecies(speciesName))
                {
                    if (Prey.Subtype.category == SubtypeCategory.Young)
                    {
                        anim.Play(start_small);
                    }
                    else
                    {
                        anim.Play(start_success);
                    }
                }
                break;
            }
        }

        int GetBaseDamagePrey()
        {
            switch (Prey.Species.name.ToLower())
            {
                case "mule deer": return 2;
                case "pronghorn": return 2;
                case "elk": return 4;
                case "moose": return 6;
                case "bison": return 10;
            }

            return 1;
        }

        void Update()
        {
            Wolf.Teleport(posPlayer, Wolf.Rotation, gm, false);
            Prey.Teleport(posPrey, Prey.Rotation, gm, false);

            if (anim.IsPlaying(start_small))
            {
                if (anim[start_small].normalizedTime >= .95f)
                {
                    Wolf.State.Food += (Prey.State.MaxHealth * .1f);
                    if (Wolf.State.Food > Wolf.State.MaxFoodWithoutRegurgitant)
                    {
                        Wolf.State.Food = Wolf.State.MaxFoodWithoutRegurgitant;
                    }

                    Prey.TakeDamage(
                        9999,
                        false, false, false,
                        DamageType.Blunt, AttackSegment.MidRight, Wolf);

                    string speciesName = Prey.Species.name.ToLower();
                    if (speciesName == "fox" || speciesName == "coyote")
                    {
                        Prey.Teleport(Vector3.zero + (Vector3.up * 500), Quaternion.identity, gm, false);
                    }

                    Destroy(gameObject);
                }

                return;
            }

            if (anim.IsPlaying(start_failed))
            {
                if (anim[start_failed].normalizedTime >= .95f)
                {
                    Destroy(gameObject);
                }

                return;
            }

            if (Input.GetKeyDown(KeyCode.Space))
            {
                au.clip = Plugin.sndFail;
                au.Play();
                anim.Play(start_failed);
                return;
            }


            if (anim.IsPlaying(loop_bite))
            {
                tmrBiteSound -= Time.unscaledTime;
                if (tmrBiteSound < 0)
                {
                    au.clip = Plugin.sndBite;
                    au.Play();
                    tmrBiteSound = au.clip.length + 1;
                }

                tmrDamageBite -= Time.unscaledTime;
                if (tmrDamageBite < 0)
                {
                    tmrDamageBite = .25f;

                    trueHealth_wolf -= preyDamage * damageMult;
                    trueHealth_prey -= wolfDamage * damageMult;
                }

                health_prey.fillAmount = trueHealth_prey / Prey.State.MaxHealth;
                health_wolf.fillAmount = trueHealth_wolf / Wolf.State.MaxHealth;

                if (trueHealth_prey < 0)
                {
                    Prey.TakeDamage(
                        9999,
                        false, false, false,
                        DamageType.Blunt, AttackSegment.MidRight, Wolf);

                    au.clip = null;
                    switch (Prey.Species.name.ToLower())
                    {
                        case "mule deer": au.clip = Plugin.sndDeer; break;
                        case "pronghorn": au.clip = Plugin.sndDeer; break;
                        case "elk": au.clip = Plugin.sndElk; break;
                        case "moose": au.clip = Plugin.sndMoose; break;
                        case "bison": au.clip = Plugin.sndBison; break;
                    }

                    if (au.clip != null) au.Play(100);

                    anim.Play(end_success);
                }
                else if (trueHealth_wolf < 0)
                {
                    Wolf.TakeDamage(
                        (int)(Wolf.State.MaxHealth * .25f),
                        false, false, false,
                        DamageType.Blunt, AttackSegment.MidRight, Prey);

                    au.clip = Plugin.sndFail;
                    au.Play();

                    anim.Play(end_failed);
                }

                return;
            }

            if (anim.IsPlaying(start_success))
            {
                if (anim[start_success].normalizedTime >= .95f)
                {
                    anim.Play(loop_bite);
                }
            }
            else if (anim.IsPlaying(end_failed))
            {
                if (anim[end_failed].normalizedTime >= .95f)
                {
                    Destroy(gameObject);
                }
            }
            else if (anim.IsPlaying(end_success))
            {
                if (anim[end_success].normalizedTime >= .95f)
                {
                    Destroy(gameObject);
                }
            }
        }

        float GetWolfDamage()
        {
            float result = Mathf.Max(1f, Wolf.WolfDef.innateAbilities.strength);

            if (Globals.Integers.ContainsKey("rp_strength"))
            {
                result += Globals.Integers["rp_strength"] * .2f;
            }

            foreach (Animal packmember in Wolf.Pack.PlayerPackData.Members)
            {
                if (packmember == Wolf) continue;

                if (Vector3.Distance(packmember.Position, Wolf.Position) < maxDistanceForHuntBonus)
                {
                    switch (packmember.WolfDef.lifeStage)
                    {
                        case WolfLifeStage.YoungHunter: result *= 1.05f; break;
                        case WolfLifeStage.Yearling: result *= 1.1f; break;
                        case WolfLifeStage.Adult: result *= 1.25f; break;
                    }
                }
            }

            Globals.Log($"Calculated wolf damage multiplier: {result}");
            return result;
        }

        void OnDestroy()
        {
            InputControls.DisableCameraInput = false;
            InputControls.DisableInput = false;
        }
    }
}
