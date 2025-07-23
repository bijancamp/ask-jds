export interface ChatRequest {
  message: string;
  history: ChatMessage[];
}

export interface ChatResponse {
  Response: string;
  Sources: DocumentSource[];
  ConversationId: string;
}

export interface ChatMessage {
  role: 'user' | 'assistant';
  content: string;
  timestamp: Date;
}

export interface DocumentSource {
  id: string;
  title: string;
  company: string;
  excerpt: string;
  score: number;
}