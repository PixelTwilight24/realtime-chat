import { Routes } from '@angular/router';
import { Login } from '../features/login/login';
import { Signup } from '../features/signup/signup';
import { ForgotPassword } from '../features/forgot-password/forgot-password';
import { ChangePassword } from '../features/change-password/change-password';
import { ResetPassword } from '../features/reset-password/reset-password';
import { Chat } from '../features/chat/chat';
import { authGuard } from '../core/guards/auth-guard';

export const routes: Routes = [
    { path: '', redirectTo: 'login', pathMatch: 'full' },
    { path: 'login', component: Login },
    { path: 'signup', component: Signup },
    { path: 'forgot-password', component: ForgotPassword },
    { path: 'change-password', component: ChangePassword },
    { path: 'reset-password', component: ResetPassword },
    { path: 'chat', component: Chat, canActivate: [authGuard] },
];
