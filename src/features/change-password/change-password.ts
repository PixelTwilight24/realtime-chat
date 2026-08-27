import { Component, inject } from '@angular/core';
import { ReactiveFormsModule, FormControl, FormGroup, Validators } from '@angular/forms';
import { Router } from '@angular/router';

@Component({
  selector: 'app-change-password',
  imports: [ReactiveFormsModule],
  templateUrl: './change-password.html',
  styleUrl: './change-password.css',
})
export class ChangePassword {
  private router = inject(Router);

  changeForm = new FormGroup(
    {
      currentPassword: new FormControl('', [Validators.required]),
      newPassword: new FormControl('', [Validators.required, Validators.minLength(6)]),
      confirmPassword: new FormControl('', [Validators.required]),
    },
    this.passwordMatchValidator
  );

  passwordMatchValidator(form: any) {
    const newPass = form.get('newPassword')?.value;
    const confirm = form.get('confirmPassword')?.value;

    return newPass === confirm ? null : { passwordMismatch: true };
  }

  onSubmit() {
    if (this.changeForm.invalid) return;

    console.log('Change password data:', this.changeForm.value);
  }

  goBack() {
    this.router.navigate(['/login']);
  }
}
