using UnityEngine;

public class CameraMenuSway : MonoBehaviour
{
    public float amount = 0.2f; 
    public float speed = 1.5f; 
    private Vector3 startPosition;
    
    void Start()
    {
        startPosition = transform.position;
    }

    void Update()
{   
        float x = Mathf.Sin(Time.time * speed) * amount;
        float y = Mathf.Cos(Time.time * speed * 0.8f) * amount; 
        transform.position = startPosition + new Vector3(x, y, 0);
    }
}