using UnityEngine;

public class Pancake : MonoBehaviour
{
    public int sizeValue;
    [HideInInspector] public Vector3 stackPosition;

    void Awake()
    {
        // Fizik motorunun pankekleri düşürmesini engelle
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        // Modelin kendi animasyonu varsa pozisyonu kilitlemesini engelle
        Animator anim = GetComponentInChildren<Animator>();
        if (anim != null)
        {
            anim.enabled = false;
        }
    }

    public void Setup(int size, float scaleXZ, float scaleY)
    {
        sizeValue = size;
        transform.localScale = new Vector3(scaleXZ, scaleY, scaleXZ);
    }

    public void SetStackPosition(Vector3 pos)
    {
        stackPosition = pos;
        transform.position = pos;
        transform.rotation = Quaternion.identity;
    }

    public void SetSelectedVisual(bool selected)
    {
        // Seçilen pankek sabit kalır, yerinden oynamaz
        transform.position = stackPosition;
    }
}