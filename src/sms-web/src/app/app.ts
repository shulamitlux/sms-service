import { Component, viewChild } from '@angular/core';
import { MessagesTableComponent } from './messages-table.component';
import { SendFormComponent } from './send-form.component';

@Component({
  selector: 'app-root',
  imports: [SendFormComponent, MessagesTableComponent],
  template: `
    <main>
      <h1>שירות SMS לעובדים</h1>
      <app-send-form (sent)="table().reload()" />
      <app-messages-table />
    </main>
  `,
})
export class App {
  protected readonly table = viewChild.required(MessagesTableComponent);
}
