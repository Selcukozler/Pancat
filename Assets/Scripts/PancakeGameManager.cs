using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class PancakeGameManager : MonoBehaviour
{
    [Header("Referanslar")]
    public Pancake pancakePrefab;
    public Transform plateTransform;

    [Header("Seviye ve Değme Ayarları")]
    public int currentLevel = 1;
    [Tooltip("Pankeklerin birbirine ne kadar sıkı değeceği (1.0 = Tam değer, 0.85 = Hafif iç içe/yumuşak oturur)")]
    public float contactMultiplier = 0.88f; 
    [Tooltip("En alttaki pankekin tabağa değme yüksekliği")]
    public float plateSurfaceOffset = 0.08f;

    [Header("Pankek Boyut Ayarları")]
    public float minScale = 0.65f;
    public float scaleStep = 0.28f;
    public float thicknessY = 0.85f;

    [Header("Oyun Modu ve Animasyon")]
    [Tooltip("İşaretliyse: Tıklanan pankekten en üste kadar olan grup takla atarak ters döner (Klasik Pancake Sort).\nİşaretli değilse: Seçilen 2 pankek yer değiştirir.")]
    public bool realPancakeSortMode = false;
    public float flipDuration = 0.45f;
    public float arcWidth = 1.5f;

    private List<Pancake> stack = new List<Pancake>();
    private Pancake firstSelected = null;
    private bool isBusy = false;
    private bool showLevelCompleteUI = false;
    private int moveCount = 0;
    private float measuredPancakeHeight = 0.35f;

    void Start()
    {
        if (plateTransform == null)
        {
            GameObject plateObj = GameObject.Find("Circle Plate 2");
            if (plateObj != null) plateTransform = plateObj.transform;
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

        // Level 1 = 3 Pankek, Level 2 = 4 Pankek, Level 3 = 5 Pankek...
        int pancakeCount = level + 2;

        List<int> sizes = new List<int>();
        for (int i = 1; i <= pancakeCount; i++)
        {
            sizes.Add(i);
        }

        // Zaten sıralı gelmeyecek şekilde karıştır
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

            // İlk pankekin gerçek 3D yüksekliğini ölç (birbirine tam değmeleri için)
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
    }

    void OnPancakeClicked(Pancake clicked)
    {
        if (realPancakeSortMode)
        {
            int index = stack.IndexOf(clicked);
            if (index >= 0 && index < stack.Count - 1)
            {
                moveCount++;
                StartCoroutine(AnimatePancakeFlipGroup(index));
            }
        }
        else
        {
            if (firstSelected == null)
            {
                firstSelected = clicked;
                firstSelected.SetSelectedVisual(true);
            }
            else if (firstSelected == clicked)
            {
                firstSelected.SetSelectedVisual(false);
                firstSelected = null;
            }
            else
            {
                moveCount++;
                StartCoroutine(AnimateSwapTwoFlip(firstSelected, clicked));
                firstSelected = null;
            }
        }
    }

    // Seçilen 2 pankekin takla atarak yer değiştirmesi
    IEnumerator AnimateSwapTwoFlip(Pancake pancakeA, Pancake pancakeB)
    {
        isBusy = true;

        pancakeA.SetSelectedVisual(false);
        pancakeB.SetSelectedVisual(false);

        int indexA = stack.IndexOf(pancakeA);
        int indexB = stack.IndexOf(pancakeB);

        Vector3 posA = GetSlotPosition(indexA);
        Vector3 posB = GetSlotPosition(indexB);

        float elapsed = 0f;
        while (elapsed < flipDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / flipDuration);
            float smoothT = Mathf.SmoothStep(0f, 1f, t);

            float arcOffset = Mathf.Sin(smoothT * Mathf.PI) * arcWidth;

            Vector3 currentPosA = Vector3.Lerp(posA, posB, smoothT);
            currentPosA.x += arcOffset;

            Vector3 currentPosB = Vector3.Lerp(posB, posA, smoothT);
            currentPosB.x -= arcOffset;

            pancakeA.transform.position = currentPosA;
            pancakeB.transform.position = currentPosB;

            float flipAngle = smoothT * 360f;
            pancakeA.transform.rotation = Quaternion.Euler(0f, 0f, -flipAngle);
            pancakeB.transform.rotation = Quaternion.Euler(0f, 0f, flipAngle);

            yield return null;
        }

        stack[indexA] = pancakeB;
        stack[indexB] = pancakeA;

        RefreshPositions();

        yield return CheckWinRoutine();
        isBusy = false;
    }

    // Klasik Pancake Sort: Seçilen indexten en üste kadar olan grubu ters çevirir
    IEnumerator AnimatePancakeFlipGroup(int startIndex)
    {
        isBusy = true;

        int topIndex = stack.Count - 1;
        int count = stack.Count - startIndex;

        Vector3 bottomPos = GetSlotPosition(startIndex);
        Vector3 topPos = GetSlotPosition(topIndex);
        Vector3 centerPivot = (bottomPos + topPos) * 0.5f;

        GameObject pivotObj = new GameObject("FlipPivot");
        pivotObj.transform.position = centerPivot;

        for (int i = startIndex; i <= topIndex; i++)
        {
            stack[i].transform.SetParent(pivotObj.transform, true);
        }

        float elapsed = 0f;
        while (elapsed < flipDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / flipDuration);
            float smoothT = Mathf.SmoothStep(0f, 1f, t);

            float heightBoost = Mathf.Sin(smoothT * Mathf.PI) * 0.8f;
            pivotObj.transform.position = centerPivot + Vector3.up * heightBoost;
            pivotObj.transform.rotation = Quaternion.Euler(0f, 0f, smoothT * 180f);

            for (int i = startIndex; i <= topIndex; i++)
            {
                stack[i].transform.rotation = Quaternion.identity;
            }

            yield return null;
        }

        for (int i = startIndex; i <= topIndex; i++)
        {
            stack[i].transform.SetParent(null, true);
        }
        Destroy(pivotObj);

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
            // Seviye atlamadan önce oyuncunun dizilimi görmesi için 1.2 saniye bekle
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
        // En altta (index 0) en büyük pankek, en üstte en küçük pankek olmalı (Piramit dizilimi)
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