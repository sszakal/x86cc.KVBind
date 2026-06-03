import { Component } from '@angular/core';
import { Router, RouterModule } from '@angular/router';
import { UserService } from '../../../core/services/user.service';
import { ThemeService } from '../../../core/services/theme.service';

@Component({
  selector: 'app-layout',
  imports: [RouterModule],
  templateUrl: './app-layout.component.html',
})
export class AppLayoutComponent {
  constructor(
    readonly userService: UserService,
    readonly theme: ThemeService,
    private readonly router: Router,
  ) {}

  logout(): void {
    this.userService.clearUser();
    this.router.navigate(['/login']);
  }
}
