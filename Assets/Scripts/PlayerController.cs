using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    Rigidbody rb = null;
    Vector3 direction = Vector3.zero;
    [SerializeField]
    float velocity = 0.0f;
    // Start is called before the first frame update
    void Start()
    {
        rb = GetComponent<Rigidbody>();
        //if (gameObject.name == FindObjectOfType<NetworkMan>().myAddress)
        //{
        //    active = true;
        //}
    }
    // Update is called once per frame
    void Update()
    {
        direction = new Vector3(Input.GetAxis("Horizontal"), 0.0f, Input.GetAxis("Vertical")).normalized;
    }

    private void FixedUpdate()
    {
        rb.velocity = velocity * direction;  // calc directional velocity
    }
}
