import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';

import { routes } from './app.routes';
import { FaIconLibrary } from '@fortawesome/angular-fontawesome';
import { registerIcons } from '../core/Icons/icon';
import { authInterceptor } from '../core/interceptors/auth-interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    provideHttpClient(withInterceptors([authInterceptor])),
    {
      provide: FaIconLibrary,
      useFactory: () => {
        const lib = new FaIconLibrary();
        registerIcons(lib);
        return lib;
      }
    }
  ]
};
