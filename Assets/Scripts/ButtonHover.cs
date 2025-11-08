using UnityEngine;
using UnityEngine.EventSystems;

public class UIButtonHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    Vector3 normalScale;
    Vector3 targetScale;
    [SerializeField] float scaleFactor = 1.05f;
    [SerializeField] float speed = 8f;

    void Start()
    {
        normalScale = transform.localScale;
        targetScale = normalScale;
    }

    public void OnPointerEnter(PointerEventData e)
    {
        targetScale = normalScale * scaleFactor;
    }

    public void OnPointerExit(PointerEventData e)
    {
        targetScale = normalScale;
    }

    void Update()
    {
        transform.localScale = Vector3.Lerp(transform.localScale, targetScale, Time.deltaTime * speed);
    }
}