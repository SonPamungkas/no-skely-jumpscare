using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine.Networking;

namespace SkeletonJumpscare
{
    public class JumpscareVariant
    {
        public Texture2D texture;
        public int frames;
    }

    [BepInPlugin("neutral.skeletonjumpscare", "Skeleton Jumpscare", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public ConfigEntry<int> IntervalSeconds;
        public ConfigEntry<int> ActivationChance;
        public ConfigEntry<float> JumpscareVolume;

        private AudioClip jumpscareAudio;
        private AudioSource audioSource;

        private List<JumpscareVariant> variants = new List<JumpscareVariant>();
        private JumpscareVariant currentVariant;

        private float timer = 0f;
        private float bufferedCooldown = 0f;
        private bool bufferedRequest = false;
        private bool isJumpscaring = false;
        private float jumpscareTime = 0f;
        private const float framesPerSecond = 24f;

        private Vector3 prevMousePos;
        private float afkTimer = 0f;

        private void Awake()
        {
            IntervalSeconds = Config.Bind("General", "IntervalSeconds", 1, new ConfigDescription("Interval in seconds to check for jumpscare", new AcceptableValueRange<int>(1, 60)));
            ActivationChance = Config.Bind("General", "ActivationChance", 10000, new ConfigDescription("1 in X chance to trigger jumpscare", new AcceptableValueRange<int>(1, 100000)));
            JumpscareVolume = Config.Bind("General", "JumpscareVolume", 1f, new ConfigDescription("Volume of the jumpscare", new AcceptableValueRange<float>(0f, 1f)));

            LoadTexture("SkeletonJumpscare.Resources.skeleton-across-left_spritesheet.png", 26);
            LoadTexture("SkeletonJumpscare.Resources.skeleton-across-right_spritesheet.png", 26);
            LoadTexture("SkeletonJumpscare.Resources.skeleton-incoming-left_spritesheet.png", 23);
            LoadTexture("SkeletonJumpscare.Resources.skeleton-incoming-right_spritesheet.png", 23);
            StartCoroutine(LoadAudio());

            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;

            prevMousePos = Input.mousePosition;
            Logger.LogInfo("SkeletonJumpscare plugin loaded.");
        }

        private void LoadTexture(string resourceName, int frameCount)
        {
            var assembly = Assembly.GetExecutingAssembly();
            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream != null)
                {
                    byte[] data = new byte[stream.Length];
                    stream.Read(data, 0, data.Length);
                    Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    tex.LoadImage(data);
                    tex.filterMode = FilterMode.Bilinear;
                    tex.wrapMode = TextureWrapMode.Clamp;
                    variants.Add(new JumpscareVariant { texture = tex, frames = frameCount });
                }
                else
                {
                    Logger.LogError($"Could not find embedded resource: {resourceName}");
                }
            }
        }

        private IEnumerator LoadAudio()
        {
            var assembly = Assembly.GetExecutingAssembly();
            string tempPath = Path.Combine(Paths.CachePath, "temp_skeletonjumpscare.ogg");

            using (Stream stream = assembly.GetManifestResourceStream("SkeletonJumpscare.Resources.bttb.ogg"))
            {
                if (stream != null)
                {
                    Directory.CreateDirectory(Paths.CachePath);
                    using (FileStream fs = new FileStream(tempPath, FileMode.Create))
                    {
                        stream.CopyTo(fs);
                    }
                }
                else
                {
                    Logger.LogError("Could not find embedded resource: SkeletonJumpscare.Resources.bttb.ogg");
                    yield break;
                }
            }

            string uri = "file:///" + tempPath.Replace("\\", "/");
            using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(uri, AudioType.OGGVORBIS))
            {
                yield return www.SendWebRequest();

                if (www.result == UnityWebRequest.Result.ConnectionError || www.result == UnityWebRequest.Result.ProtocolError)
                {
                    Logger.LogError($"UnityWebRequest error: {www.error}");
                }
                else
                {
                    jumpscareAudio = DownloadHandlerAudioClip.GetContent(www);
                }
            }
            
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            timer += dt;

            if (bufferedCooldown > 0f)
            {
                bufferedCooldown -= dt;
            }

            if (Input.mousePosition != prevMousePos || Input.anyKey)
            {
                afkTimer = 0f;
                prevMousePos = Input.mousePosition;
            }
            else
            {
                afkTimer += dt;
            }

            if (bufferedCooldown <= 0f && timer >= IntervalSeconds.Value)
            {
                timer = 0f;
                if (!isJumpscaring && UnityEngine.Random.Range(0, ActivationChance.Value) == 0)
                {
                    if (Application.isFocused && afkTimer < 25f)
                    {
                        TriggerJumpscare();
                    }
                    else if (!bufferedRequest)
                    {
                        bufferedRequest = true;
                        bufferedCooldown = 2f + UnityEngine.Random.Range(0f, 28f);
                    }
                }
            }

            if (bufferedRequest && Application.isFocused && afkTimer < 25f && bufferedCooldown <= 0f && !isJumpscaring)
            {
                bufferedRequest = false;
                TriggerJumpscare();
            }

            if (isJumpscaring)
            {
                jumpscareTime += dt;
                if (jumpscareTime * framesPerSecond >= currentVariant.frames)
                {
                    isJumpscaring = false;
                }
            }
        }

        private int variantIndex = 0;

        private void TriggerJumpscare()
        {
            if (variants.Count == 0) return;
            
            isJumpscaring = true;
            jumpscareTime = 0f;
            currentVariant = variants[variantIndex];
            variantIndex = (variantIndex + 1) % variants.Count;
            if (jumpscareAudio != null)
            {
                audioSource.volume = JumpscareVolume.Value;
                audioSource.PlayOneShot(jumpscareAudio);
            }
        }

        private void OnGUI()
        {
            if (isJumpscaring && currentVariant != null && currentVariant.texture != null)
            {
                int frame = (int)(jumpscareTime * framesPerSecond);
                frame = Mathf.Clamp(frame, 0, currentVariant.frames - 1);
                
                float frameHeightUV = 1f / currentVariant.frames;
                float yUV = 1f - (frame + 1) * frameHeightUV;
                Rect texCoords = new Rect(0f, yUV, 1f, frameHeightUV);

                float framePixelHeight = (float)currentVariant.texture.height / currentVariant.frames;
                float drawHeight = Screen.width * (framePixelHeight / currentVariant.texture.width);
                float yPos = (Screen.height - drawHeight) / 2f;

                GUI.DrawTextureWithTexCoords(new Rect(0, yPos, Screen.width, drawHeight), currentVariant.texture, texCoords, true);
            }
        }
    }
}
