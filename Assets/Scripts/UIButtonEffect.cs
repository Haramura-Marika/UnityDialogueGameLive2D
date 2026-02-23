using UnityEngine;
using UnityEngine.EventSystems;

public class UIButtonEffect : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    public float scaleFactor = 1.1f;
    public float duration = 0.1f;

    private Vector3 initialScale;
    private bool isHovering;

    void Start()
    {
        initialScale = transform.localScale;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        isHovering = true;
        StopAllCoroutines();
        StartCoroutine(ScaleTo(initialScale * scaleFactor));
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isHovering = false;
        StopAllCoroutines();
        StartCoroutine(ScaleTo(initialScale));
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        StopAllCoroutines();
        StartCoroutine(ScaleTo(initialScale * 0.95f));
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        StopAllCoroutines();
        StartCoroutine(ScaleTo(isHovering ? initialScale * scaleFactor : initialScale));
    }

    System.Collections.IEnumerator ScaleTo(Vector3 targetScale)
    {
        float time = 0;
        Vector3 startScale = transform.localScale;
        while (time < duration)
        {
            transform.localScale = Vector3.Lerp(startScale, targetScale, time / duration);
            time += Time.unscaledDeltaTime;
            yield return null;
        }
        transform.localScale = targetScale;
    }
}
