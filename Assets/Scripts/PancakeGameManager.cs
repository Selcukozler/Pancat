using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class PancakeGameManager : MonoBehaviour
{
    [Header("Referanslar")]
    public Pancake pancakePrefab;
    public Transform plateTransform;
    public Transform spatulaTransform;

    [Header("Spatula Hedef Konum Ayarları")]
    [Tooltip("Pankek seçildiğinde spatulanın pankekin altında duracağı göreceli mesafe")]
    public Vector3 spatulaActiveOffset = new Vector3(0f, -0.05f, -0.8f);

    [Header("Seviye ve Değme Ayarları")]
    public int currentLevel = 1;
    [Tooltip("Pankeklerin birbirine ne kadar sıkı değeceği (0.85 = Tam oturur)")]
    public float contactMultiplier = 0.85f;
    [Tooltip("En alttaki pankekin tabağa değme yüksekliği")]
    public float plateSurfaceOffset = 0.08f;

    [Header("Pankek Boyut Ayarları")]
    public float minScale = 0.65f;
    public float scaleStep = 0.28f;
    public float thicknessY = 0.85f;

    [Header("Animasyon Ayarları")]
    public float flipDuration = 0.5f;
    public float arcWidth = 1.2f;

    private List<Pancake> stack = new List<Pancake>();
    private Pancake firstSelected = null;
    private bool isBusy = false;
    private bool showLevelCompleteUI = false;
    private int moveCount = 0;
    private float measuredPancakeHeight = 0.35f;

    // Spatulanın sahnede Inspector'da bıraktığın orijinal başlangıç konum ve açısı
    private Vector3 spatulaRestPosition;
    private Quaternion spatulaRestRotation;

    void Start()
    {
        if (plateTransform == null)
        {
            GameObject plateObj = GameObject.Find("Circle Plate 2");
            if (plateObj != null) plateTransform = plateObj.transform;
        }

        if (spatulaTransform == null)
        {
            GameObject spatObj = GameObject.Find("Spatula_01a");
            if (spatObj != null) spatulaTransform = spatObj.transform;
        }

        // Spatulanın sahnede durduğu ilk konum ve açısını otomatik olarak hafızaya al
        if (spatulaTransform != null)
        {
            spatulaRestPosition = spatulaTransform.position;
            spatulaRestRotation = spatulaTransform.rotation;

            Collider[] cols = spatulaTransform.GetComponentsInChildren<Collider>();
            foreach (var c in cols) c.enabled = false;
        }

        Pancake[] existing = FindObjectsByType<Pancake>(FindObjectsSortMode.None);
        foreach (var p in existing)
        {
            Destroy(p.gameObject);
        }

        StartLevel(currentLevel);
    }

    void Update()
    {
        if (isBusy) return;

        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            DetectPancakeClick();
        }
    }

    void DetectPancakeClick()
    {
        Vector2 mousePos = Mouse.current.position.ReadValue();
        Ray ray = Camera.main.ScreenPointToRay(mousePos);

        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            Pancake clicked = hit.collider.GetComponentInParent<Pancake>();
            if (clicked != null && stack.Contains(clicked))
            {
                OnPancakeClicked(clicked);
            }
        }
    }

    public void StartLevel(int level)
    {
        foreach (var p in stack)
        {
            if (p != null) Destroy(p.gameObject);
        }
        stack.Clear();
        firstSelected = null;
        moveCount = 0;
        showLevelCompleteUI = false;

        int pancakeCount = level + 2;

        List<int> sizes = new List<int>();
        for (int i = 1; i <= pancakeCount; i++)
        {
            sizes.Add(i);
        }

        do
        {
            for (int i = 0; i < sizes.Count; i++)
            {
                int rnd = Random.Range(0, sizes.Count);
                int temp = sizes[i];
                sizes[i] = sizes[rnd];
                sizes[rnd] = temp;
            }
        } while (IsListSorted(sizes));

        for (int i = 0; i < pancakeCount; i++)
        {
            Pancake newPancake = Instantiate(pancakePrefab);
            float currentScaleXZ = minScale + ((sizes[i] - 1) * scaleStep);
            newPancake.Setup(sizes[i], currentScaleXZ, thicknessY);

            if (i == 0)
            {
                Renderer rend = newPancake.GetComponentInChildren<Renderer>();
                if (rend != null)
                {
                    measuredPancakeHeight = rend.bounds.size.y * contactMultiplier;
                }
            }

            stack.Add(newPancake);
        }

        RefreshPositions();
        ResetSpatulaToRest();
    }

    void ResetSpatulaToRest()
    {
        if (spatulaTransform != null)
        {
            // Spatula tabağın yeni konumuna göre ayarladığın ilk yerine geri döner
            spatulaTransform.position = spatulaRestPosition;
            spatulaTransform.rotation = spatulaRestRotation;
        }
    }

    void OnPancakeClicked(Pancake clicked)
    {
        if (firstSelected == null)
        {
            firstSelected = clicked;
            // Spatula ilk konumundan kalkıp seçilen pankekin altına kayarak gelir
            if (spatulaTransform != null)
            {
                StartCoroutine(AnimateSpatulaMove(spatulaRestPosition, clicked.stackPosition + spatulaActiveOffset, 0.2f));
            }
        }
        else if (firstSelected == clicked)
        {
            firstSelected = null;
            ResetSpatulaToRest();
        }
        else
        {
            int indexA = stack.IndexOf(firstSelected);
            int indexB = stack.IndexOf(clicked);

            int startIndex = Mathf.Min(indexA, indexB);
            int endIndex = Mathf.Max(indexA, indexB);

            firstSelected = null;
            moveCount++;

            StartCoroutine(AnimateFlipRangeRoutine(startIndex, endIndex));
        }
    }

    IEnumerator AnimateSpatulaMove(Vector3 startPos, Vector3 endPos, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            if (spatulaTransform != null)
            {
                spatulaTransform.position = Vector3.Lerp(startPos, endPos, Mathf.SmoothStep(0f, 1f, t));
                spatulaTransform.rotation = spatulaRestRotation;
            }
            yield return null;
        }
    }

    IEnumerator AnimateFlipRangeRoutine(int startIndex, int endIndex)
    {
        isBusy = true;

        int count = (endIndex - startIndex) + 1;

        Vector3 bottomPos = GetSlotPosition(startIndex);
        Vector3 topPos = GetSlotPosition(endIndex);
        Vector3 centerPivot = (bottomPos + topPos) * 0.5f;

        Vector3 targetSpatulaPos = bottomPos + spatulaActiveOffset;
        if (spatulaTransform != null)
        {
            spatulaTransform.position = targetSpatulaPos;
            spatulaTransform.rotation = spatulaRestRotation;
        }

        GameObject pivotObj = new GameObject("RangeFlipPivot");
        pivotObj.transform.position = centerPivot;

        for (int i = startIndex; i <= endIndex; i++)
        {
            stack[i].transform.SetParent(pivotObj.transform, true);
        }

        float elapsed = 0f;
        while (elapsed < flipDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / flipDuration);
            float smoothT = Mathf.SmoothStep(0f, 1f, t);

            float sideOffset = -Mathf.Sin(smoothT * Mathf.PI) * arcWidth;
            pivotObj.transform.position = centerPivot + new Vector3(sideOffset, 0f, 0f);
            pivotObj.transform.rotation = Quaternion.Euler(0f, 0f, smoothT * 180f);

            for (int i = startIndex; i <= endIndex; i++)
            {
                stack[i].transform.rotation = Quaternion.identity;
            }

            if (spatulaTransform != null)
            {
                float spatulaLift = Mathf.Sin(smoothT * Mathf.PI) * 0.3f;
                spatulaTransform.position = targetSpatulaPos + new Vector3(sideOffset, spatulaLift, 0f);
                spatulaTransform.rotation = spatulaRestRotation;
            }

            yield return null;
        }

        for (int i = startIndex; i <= endIndex; i++)
        {
            stack[i].transform.SetParent(null, true);
        }

        Destroy(pivotObj);
        
        // Çevirme bittikten sonra spatula akıcı bir şekilde orijinal yerine geri döner
        if (spatulaTransform != null)
        {

            yield return AnimateSpatulaMove(spatulaTransform.position, spatulaRestPosition, 0.25f);
        }

        ResetSpatulaToRest();

        stack.Reverse(startIndex, count);
        RefreshPositions();

        yield return CheckWinRoutine();
        isBusy = false;
    }

    IEnumerator CheckWinRoutine()
    {
        if (IsStackSorted())
        {
            showLevelCompleteUI = true;
            yield return new WaitForSeconds(1.2f);
            currentLevel++;
            StartLevel(currentLevel);
        }
    }

    Vector3 GetSlotPosition(int index)
    {
        Vector3 basePos = plateTransform != null ? plateTransform.position : Vector3.zero;
        return new Vector3(
            basePos.x,
            basePos.y + plateSurfaceOffset + (index * measuredPancakeHeight),
            basePos.z
        );
    }

    void RefreshPositions()
    {
        for (int i = 0; i < stack.Count; i++)
        {
            stack[i].SetStackPosition(GetSlotPosition(i));
        }
    }

    bool IsListSorted(List<int> list)
    {
        for (int i = 0; i < list.Count - 1; i++)
        {
            if (list[i] < list[i + 1]) return false;
        }
        return true;
    }

    bool IsStackSorted()
    {
        for (int i = 0; i < stack.Count - 1; i++)
        {
            if (stack[i].sizeValue < stack[i + 1].sizeValue)
                return false;
        }
        return true;
    }

    void OnGUI()
    {
        GUIStyle infoStyle = new GUIStyle(GUI.skin.label);
        infoStyle.fontSize = 26;
        infoStyle.fontStyle = FontStyle.Bold;
        infoStyle.normal.textColor = Color.white;

        GUI.Label(new Rect(25, 25, 350, 40), $"LEVEL: {currentLevel}  ({stack.Count} Pankek)", infoStyle);
        GUI.Label(new Rect(25, 60, 350, 40), $"HAMLE: {moveCount}", infoStyle);

        if (showLevelCompleteUI)
        {
            GUIStyle winStyle = new GUIStyle(GUI.skin.box);
            winStyle.fontSize = 34;
            winStyle.fontStyle = FontStyle.Bold;
            winStyle.alignment = TextAnchor.MiddleCenter;
            winStyle.normal.textColor = Color.yellow;

            GUI.Box(new Rect(Screen.width / 2f - 220, Screen.height / 2f - 50, 440, 100),
                $"LEVEL {currentLevel} TAMAMLANDI!\nSonraki Seviye Yükleniyor...", winStyle);
        }
    }
}