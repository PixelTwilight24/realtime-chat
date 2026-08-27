import { Component, inject } from '@angular/core';
import { ReactiveFormsModule, FormControl, FormGroup, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';

@Component({
  selector: 'app-reset-password',
  imports: [ReactiveFormsModule],
  templateUrl: './reset-password.html',
  styleUrl: './reset-password.css',
})
export class ResetPassword {
  private router = inject(Router);
  private route = inject(ActivatedRoute);

  // Comes from the reset link's ?token= query param (sent via the forgot-password email). Not wired up yet.
  token = this.route.snapshot.queryParamMap.get('token');

  resetForm = new FormGroup(
    {
      newPassword: new FormControl('', [Validators.required, Validators.minLength(6)]),
      confirmPassword: new FormControl('', [Validators.required]),
    },
    this.passwordMatchValidator
  );

  passwordMatchValidator(form: any) {
    const newPassword = form.get('newPassword')?.value;
    const confirm = form.get('confirmPassword')?.value;

    return newPassword === confirm ? null : { passwordMismatch: true };
  }

  onSubmit() {
    if (this.resetForm.invalid) return;

    console.log('Reset password data:', { token: this.token, ...this.resetForm.value });
  }

  goToLogin() {
    this.router.navigate(['/login']);
  }
}
