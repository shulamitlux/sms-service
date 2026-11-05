export const MESSAGE_STATUSES = ['Pending', 'Sending', 'Sent', 'Failed'] as const;
export type MessageStatus = (typeof MESSAGE_STATUSES)[number];

export interface SendMessageRequest {
  to: string;
  body: string;
}

export interface SendMessageResponse {
  id: string;
  status: MessageStatus;
}

export interface EmployeeName {
  firstName: string;
  lastName: string;
}

export interface Message {
  id: string;
  to: string;
  body: string;
  segments: number;
  status: MessageStatus;
  attempts: number;
  lastError: string | null;
  createdAt: string;
  updatedAt: string;
  employees: EmployeeName[];
}

export interface MessageList {
  items: Message[];
}

/** RFC 9457 validation problem returned by the API on 400. */
export interface ValidationProblem {
  title?: string;
  detail?: string;
  errors?: Record<string, string[]>;
}
