import { Component, inject, signal } from '@angular/core';
import { ReactiveFormsModule, FormControl, FormGroup, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { Auth } from '../../core/services/auth';

@Component({
  selector: 'app-signup',
  imports: [ReactiveFormsModule],
  templateUrl: './signup.html',
  styleUrl: './signup.css',
})
export class Signup {
  private router = inject(Router);
  private auth = inject(Auth);

  isSubmitting = signal(false);
  errorMessage = signal<string | null>(null);

  signupForm = new FormGroup(
    {
      username: new FormControl('', [Validators.required]),
      email: new FormControl('', [Validators.required, Validators.email]),
      password: new FormControl('', [Validators.required, Validators.minLength(6)]),
      confirmPassword: new FormControl('', [Validators.required]),
    },
    this.passwordMatchValidator
  );

  passwordMatchValidator(form: any) {
    const password = form.get('password')?.value;
    const confirm = form.get('confirmPassword')?.value;

    return password === confirm ? null : { passwordMismatch: true };
  }

  onSignup() {
    if (this.signupForm.invalid) return;

    const { username, email, password } = this.signupForm.value;
    this.errorMessage.set(null);
    this.isSubmitting.set(true);

    this.auth.signup({ name: username!, email: email!, password: password! }).subscribe({
      next: () => {
        this.isSubmitting.set(false);
        this.router.navigate(['/chat']);
      },
      error: (err: HttpErrorResponse) => {
        this.isSubmitting.set(false);
        this.errorMessage.set(err.error?.message ?? 'Unable to create account. Please try again.');
      },
    });
  }

  onBackToLogin() {
    this.router.navigate(['/login']);
  }
}
