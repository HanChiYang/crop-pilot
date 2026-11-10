import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter } from '@angular/router';

import { routes } from './app.routes';
import { bearerInterceptor } from './auth-session';
import { BASE_PATH } from './generated/variables';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideHttpClient(withInterceptors([bearerInterceptor])),
    { provide: BASE_PATH, useValue: '' },
    provideRouter(routes),
  ]
};
