using System;
using UnityEngine;

namespace SkyTide
{
    /// <summary>
    /// Все материалы прототипа. В сцене заполняются ассетами из Assets/_Project/Materials (их создаёт SceneBuilder),
    /// чтобы нужные варианты шейдеров попали в сборку. Пустые поля создаются на лету (запасной путь для редактора).
    /// </summary>
    [Serializable]
    public class GameMaterials
    {
        public Material hull;
        public Material darkWood;
        public Material brass;
        public Material iron;
        public Material canvas;
        public Material balloon;
        public Material flag;
        public Material windowGlow;
        public Material cloudSea;
        public Material cloudLayer;
        public Material cloudPuff;
        public Material skybox;
        [Tooltip("Запечённое отражение закатного неба для дерева, металла и стекла. Без обновления на телефоне.")]
        public Cubemap skyReflection;
        [Tooltip("Материал модели корабля (текстуры из Meshy). Пусто — корабль из примитивов.")]
        public Material shipBody;
        [Tooltip("Атлас облаков-спрайтов. Пусто — облака из сфер.")]
        public Material cloudSprite;

        public void EnsureAll()
        {
            if (hull == null) hull = MaterialFactory.Opaque("Hull", new Color(0.42f, 0.28f, 0.17f), 0.15f);
            if (darkWood == null) darkWood = MaterialFactory.Opaque("DarkWood", new Color(0.2f, 0.13f, 0.08f), 0.1f);
            if (brass == null) brass = MaterialFactory.Opaque("Brass", new Color(0.78f, 0.57f, 0.26f), 0.55f);
            if (iron == null) iron = MaterialFactory.Opaque("Iron", new Color(0.12f, 0.12f, 0.13f), 0.45f);
            if (canvas == null) canvas = MaterialFactory.Opaque("Canvas", new Color(0.9f, 0.82f, 0.68f), 0.05f);
            if (balloon == null) balloon = MaterialFactory.Opaque("Balloon", new Color(0.82f, 0.72f, 0.56f), 0.08f);
            if (flag == null) flag = MaterialFactory.Opaque("Flag", new Color(0.6f, 0.08f, 0.08f), 0.1f);
            if (windowGlow == null) windowGlow = MaterialFactory.WithEmission(
                MaterialFactory.Opaque("WindowGlow", new Color(1f, 0.75f, 0.3f), 0.3f), new Color(2.2f, 1.4f, 0.4f));
            if (cloudSea == null) cloudSea = MaterialFactory.Opaque("CloudSea", Color.white, 0f);
            if (cloudLayer == null) cloudLayer = MaterialFactory.Fade("CloudLayer", Color.white, 0f);
            if (cloudPuff == null) cloudPuff = MaterialFactory.Fade("CloudPuff", new Color(1f, 0.88f, 0.8f, 0.82f), 0f);
            if (skybox == null) skybox = MaterialFactory.Skybox();
        }
    }
}
