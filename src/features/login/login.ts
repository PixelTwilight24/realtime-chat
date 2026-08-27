import { Component, inject, signal } from '@angular/core';
import { ReactiveFormsModule, FormControl, FormGroup, Validators } from '@angular/forms';
import { Router, RouterOutlet } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { Auth } from '../../core/services/auth';

@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule],
  templateUrl: './login.html',
  styleUrl: './login.css',
})

export class Login {

  private router = inject(Router);
  private auth = inject(Auth);

  isSubmitting = signal(false);
  errorMessage = signal<string | null>(null);

  loginForm = new FormGroup({
    email: new FormControl('', [Validators.required, Validators.email]),
    password: new FormControl('', [Validators.required, Validators.minLength(6)]),
    rememberMe: new FormControl(false),
  });

  onLogin() {
    if (this.loginForm.invalid) {
      return;
    }

    const { email, password } = this.loginForm.value;
    this.errorMessage.set(null);
    this.isSubmitting.set(true);

    this.auth.login({ email: email!, password: password! }).subscribe({
      next: () => {
        this.isSubmitting.set(false);
        this.router.navigate(['/chat']);
      },
      error: (err: HttpErrorResponse) => {
        this.isSubmitting.set(false);
        this.errorMessage.set(err.error?.message ?? 'Unable to login. Please try again.');
      },
    });
  }

  onSignup() {
    this.router.navigate(['/signup']);
  }

  onForgotPassword() {
    this.router.navigate(['/forgot-password']);
  }
}
