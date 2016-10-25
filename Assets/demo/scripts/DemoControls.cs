using UnityEngine;
using System.Collections;

public class DemoControls : MonoBehaviour {
	public float moveSpeed;
	private Vector2 moveAmount;
	private OverheadCharacterController2D characterController;
	private SpriteRenderer spriteGraphics;
	private Animator animator;

	void Start () {
		characterController = GetComponent<OverheadCharacterController2D>();
		animator = GetComponent<Animator>();
		spriteGraphics = GetComponent<SpriteRenderer>();
	}

	void Update () {
		moveAmount = Vector2.zero;

		if (Input.GetKey(KeyCode.W))
			moveAmount.y += 1f * moveSpeed * Time.deltaTime;

		if (Input.GetKey(KeyCode.S))
			moveAmount.y += -1f * moveSpeed * Time.deltaTime;

		if (Input.GetKey(KeyCode.A))
			moveAmount.x += -1f * moveSpeed * Time.deltaTime;

		if (Input.GetKey(KeyCode.D))
			moveAmount.x += 1f * moveSpeed * Time.deltaTime;

		if (moveAmount != Vector2.zero) {
			characterController.move(moveAmount);
			spriteGraphics.flipX = (moveAmount.x > 0f) ? false : true;
			animator.Play("Run");
		} else
			animator.Play("Idle");
	}
}